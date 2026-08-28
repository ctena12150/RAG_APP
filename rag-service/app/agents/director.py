"""Director de orquesta + agentes especializados (rrhh / mantenimiento / onboarding).

- El DIRECTOR solo ve METADATOS (lista de dominios y documentos); decide a qué agente
  especializado llamar, cuántas veces y con qué consulta. Nunca ve contenido de chunks.
- Cada ESPECIALISTA es una herramienta read-only acotada a su dominio que ejecuta el
  motor de retrieval compartido (expandir → híbrido → RRF → dedupe → rerank) y devuelve
  los pasajes encontrados.
- Implementado con LangChain (tool-calling). Cualquier fallo de planificación se propaga
  hacia arriba ANTES del primer token para que el pipeline fijo tome el relevo.
"""

from __future__ import annotations

import json
import logging
import time
from collections.abc import AsyncIterator
from dataclasses import dataclass

from langchain_core.messages import AIMessage, SystemMessage, ToolMessage
from langchain_core.tools import BaseTool, tool
from langchain_openai import ChatOpenAI

from app.config import DOMINIOS, Settings
from app.core.llms import LlmClient
from app.models import Hit, Traza
from app.retrieval.engine import RetrievalEngine

logger = logging.getLogger(__name__)

ETIQUETAS_DOMINIO = {
    "rrhh": "Recursos Humanos (nóminas, vacaciones, beneficios, políticas de personal)",
    "mantenimiento": "Mantenimiento (manuales técnicos, procedimientos de equipos, calibraciones)",
    "onboarding": "Onboarding (alta de empleados, checklist, formación inicial)",
}


def _modelo_planificador(settings: Settings) -> ChatOpenAI:
    proveedor, modelo = settings.chain("planner")[0]
    base_urls = {
        "groq": (settings.groq_base_url, settings.groq_api_key),
        "mistral": (settings.mistral_base_url, settings.mistral_api_key),
        "ollama": (settings.ollama_base_url, ""),
    }
    url, key = base_urls.get(proveedor, base_urls["ollama"])
    kwargs: dict = {
        "model": modelo,
        "api_key": key or "EMPTY",
        "base_url": url,
        # sin timeout un proveedor colgado deja el chat en silencio indefinidamente
        "timeout": settings.llm_timeout_seconds,
    }
    return ChatOpenAI(**kwargs)


@dataclass
class ResultadoAgentic:
    hits: list[Hit]
    llamadas: list[dict]
    sin_busqueda: bool
    confianza: float | None = None


def crear_herramientas(
    engine: RetrievalEngine,
    settings: Settings,
    dominios: list[str] | None,
    documentos_ids: list[str] | None,
):
    """Fábrica de herramientas por dominio: cada agente queda acotado a su corpus."""
    herramientas = []
    registro_dominio: dict[str, str] = {}

    for dominio in [d for d in DOMINIOS if not dominios or d in dominios]:
        def _fabricar(dominio_fijo: str):
            async def buscar_en_agente(query: str, top_k: int = 6) -> str:
                """Descripción dinámica asignada justo después (f-string no es docstring)."""
                inicio = time.perf_counter()
                contexto = await engine.run_retrieval(
                    query,
                    historial=[],
                    dominios=[dominio_fijo],
                    documentos_ids=documentos_ids,
                    con_reescritura=False,
                )
                duracion = int((time.perf_counter() - inicio) * 1000)
                if not contexto.hits:
                    return json.dumps({"pasajes": [], "mensaje": "Sin resultados para esta consulta.", "confianza": contexto.confianza})
                pasajes = [
                    {
                        **h.to_dict(),
                        # identificador interno estable para poder fusionar tras el plan
                        "_ref": f"{h.documento_id}:{h.chunk_id}",
                    }
                    for h in contexto.hits[: max(min(top_k, 12), 1)]
                ]
                return json.dumps(
                    {"pasajes": pasajes, "duracionMs": duracion, "confianza": contexto.confianza},
                    ensure_ascii=False,
                )

            # el decorador @tool exige un docstring REAL: una f-string como primera
            # sentencia no cuenta como tal y rompía la creación de herramientas
            buscar_en_agente.__doc__ = (
                f"Búsqueda en documentos de {dominio_fijo} ({ETIQUETAS_DOMINIO[dominio_fijo]}). "
                "Devuelve los pasajes más relevantes. Úsala para preguntas sobre ese ámbito."
            )
            herramienta: BaseTool = tool(buscar_en_agente)
            herramienta.name = f"buscar_{dominio_fijo}"
            registro_dominio[herramienta.name] = dominio_fijo
            return herramienta

        herramientas.append(_fabricar(dominio))

    @tool
    async def listar_documentos() -> str:
        "Lista los documentos disponibles con sus metadatos (nombre, dominio, páginas)."
        return json.dumps(
            {"nota": "Usa esta lista para decidir a qué agente preguntar; no contiene contenido."},
            ensure_ascii=False,
        )

    herramientas.append(listar_documentos)
    return herramientas, registro_dominio


PROMPT_DIRECTOR = """Eres el DIRECTOR DE ORQUESTA de un sistema documental interno. Tu único trabajo es
decidir QUÉ agente especializado debe buscar información para responder la pregunta del usuario.

Ámbitos disponibles:
{ambitos}

Documentos indexados (SOLO metadatos; nunca verás su contenido):
{metadatos}

Reglas:
1. Para cada parte temática de la pregunta llama al agente especializado correspondiente con una
   consulta concreta y autocontenida (resuelve tú mismo las referencias como "el segundo").
2. Puedes llamar varias veces al mismo agente o a varios agentes distintos.
3. Saludos, despedidas o charla pequeña: NO llames a ninguna herramienta.
4. NUNCA respondas sobre contenido documental con tu propio conocimiento: si hay duda, busca.
5. Máximo {max_steps} rondas de herramientas.

Cuando termines de reunir información, responde brevemente qué información reuniste (sin responder la pregunta)."""


async def ejecutar_director(
    settings: Settings,
    engine: RetrievalEngine,
    llm_utilidad: LlmClient,
    pregunta: str,
    historial: list[dict],
    dominios: list[str] | None,
    documentos_ids: list[str] | None,
    traza: Traza,
) -> ResultadoAgentic:
    """Wrapper compatible: consume el generador y devuelve solo el resultado final."""
    async for item in ejecutar_director_stream(
        settings, engine, llm_utilidad, pregunta, historial, dominios, documentos_ids, traza
    ):
        if isinstance(item, ResultadoAgentic):
            return item
    raise RuntimeError("el director terminó sin resultado")


async def ejecutar_director_stream(
    settings: Settings,
    engine: RetrievalEngine,
    llm_utilidad: LlmClient,
    pregunta: str,
    historial: list[dict],
    dominios: list[str] | None,
    documentos_ids: list[str] | None,
    traza: Traza,
) -> AsyncIterator[dict | ResultadoAgentic]:
    """Bucle de planificación tool-calling en streaming.

    Emite dicts ``{"etapa": ..., ...}`` de progreso (para el evento SSE ``agent``)
    y termina cediendo el ``ResultadoAgentic`` con los pasajes fusionados."""
    herramientas, registro = crear_herramientas(engine, settings, dominios, documentos_ids)

    try:
        metadatos = await engine.metadatos_documentos()
    except Exception as exc:  # noqa: BLE001 — el fallback al pipeline fijo decide después
        raise RuntimeError(f"metadatos no disponibles: {exc}") from exc

    yield {"etapa": "planificacion", "documentos": len(metadatos)}

    ambitos = "\n".join(f"- {d}: {ETIQUETAS_DOMINIO[d]}" for d in DOMINIOS)
    sistema = PROMPT_DIRECTOR.format(
        ambitos=ambitos,
        metadatos=json.dumps(metadatos, ensure_ascii=False)[:2000],
        max_steps=settings.agentic_max_steps,
    )

    modelo = _modelo_planificador(settings).bind_tools(herramientas)
    mensajes: list = [
        SystemMessage(content=sistema),
        *[{"role": m.get("role"), "content": m.get("content", "")} for m in historial[-6:]],
        {"role": "user", "content": pregunta},
    ]

    llamadas: list[dict] = []
    hits_por_ref: dict[str, Hit] = {}
    confianzas: list[float] = []
    pasos = 0

    while pasos < settings.agentic_max_steps:
        respuesta: AIMessage = await modelo.ainvoke(mensajes)
        mensajes.append(respuesta)
        if not respuesta.tool_calls:
            break
        pasos += 1
        for llamada in respuesta.tool_calls:
            nombre = llamada["name"]
            args = llamada.get("args") or {}
            yield {"etapa": "buscando", "agente": nombre, "query": str(args.get("query", ""))[:80]}
            entrada = time.perf_counter()
            resultado_tool = await _ejecutar_herramienta(herramientas, nombre, args)
            duracion = int((time.perf_counter() - entrada) * 1000)

            datos = json.loads(resultado_tool or "{}")
            pasajes = datos.get("pasajes", []) if isinstance(datos, dict) else []
            if isinstance(datos, dict) and isinstance(datos.get("confianza"), (int, float)):
                confianzas.append(float(datos["confianza"]))
            llamadas.append(
                {
                    "agente": nombre,
                    "query": args.get("query", ""),
                    "pasajesEncontrados": len(pasajes),
                    "duracionMs": duracion,
                }
            )
            yield {"etapa": "hallazgo", "agente": nombre, "pasajes": len(pasajes), "duracionMs": duracion}
            for p in pasajes:
                # los pasajes viajan en formato wire (camelCase): reconstruir con from_dict
                hits_por_ref.setdefault(p["_ref"], Hit.from_dict({k: v for k, v in p.items() if k != "_ref"}))
            mensajes.append(
                ToolMessage(content=resultado_tool or "{}", tool_call_id=llamada["id"])
            )

    traza.agregar("planificador", llamadas=len(llamadas), detalle=llamadas)
    yield ResultadoAgentic(
        hits=list(hits_por_ref.values()),
        llamadas=llamadas,
        sin_busqueda=pasos == 0 and not llamadas,
        confianza=min(confianzas) if confianzas else None,
    )


async def _ejecutar_herramienta(herramientas: list[BaseTool], nombre: str, args: dict) -> str:
    for h in herramientas:
        if getattr(h, "name", None) == nombre:
            try:
                resultado = await h.ainvoke(args)
                return resultado if isinstance(resultado, str) else json.dumps(resultado, ensure_ascii=False)
            except Exception as exc:  # noqa: BLE001
                logger.warning("Herramienta %s falló: %s", nombre, exc)
                return json.dumps({"pasajes": [], "error": "búsqueda no disponible"})
    raise ValueError(f"Herramienta desconocida: {nombre}")

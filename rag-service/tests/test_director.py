"""Tests del Director en streaming: progreso agéntico (evento SSE "agent") y resultado."""

import asyncio

from fastapi.testclient import TestClient
from langchain_core.messages import AIMessage

from app.agents.director import ResultadoAgentic, ejecutar_director_stream
from app.main import crear_app
from app.models import Traza
from tests.doubles import contenedor_prueba, seed_documentos

CLAVE = {"X-Internal-Key": "test-key"}


class PlanificadorFalso:
    """Sustituye al ChatOpenAI del planificador: respuestas encoladas por ronda."""

    def __init__(self, respuestas: list[AIMessage]) -> None:
        self.respuestas = list(respuestas)

    def bind_tools(self, herramientas):
        return self

    async def ainvoke(self, mensajes) -> AIMessage:
        return self.respuestas.pop(0)


def test_director_stream_emite_progreso_y_resultado(monkeypatch):
    contenedor, _emb, _llm, store = contenedor_prueba()
    asyncio.run(
        seed_documentos(store, "doc-dir-1", "rrhh", ["La política establece veintitrés días de vacaciones."])
    )
    planificador = PlanificadorFalso([
        AIMessage(content="", tool_calls=[{
            "name": "buscar_rrhh",
            "args": {"query": "días de vacaciones", "top_k": 6},
            "id": "call-1",
            "type": "tool_call",
        }]),
        AIMessage(content="Información reunida."),
    ])
    monkeypatch.setattr("app.agents.director._modelo_planificador", lambda settings: planificador)

    traza = Traza(modo="agentico")

    async def recoger():
        items = []
        async for item in ejecutar_director_stream(
            contenedor.settings, contenedor.engine, contenedor.llm,
            "¿Cuántos días de vacaciones?", [], None, None, traza,
        ):
            items.append(item)
        return items

    items = asyncio.run(recoger())

    assert items and isinstance(items[-1], ResultadoAgentic)
    etapas = [i["etapa"] for i in items[:-1]]
    assert etapas[0] == "planificacion"
    assert "buscando" in etapas
    assert "hallazgo" in etapas
    buscando = next(i for i in items[:-1] if i["etapa"] == "buscando")
    assert buscando["agente"] == "buscar_rrhh"
    assert items[-1].hits  # la herramienta encontró pasajes reales del store
    assert any(e["etapa"] == "planificador" for e in traza.etapas)


def test_chat_fluye_eventos_agent_antes_de_token(monkeypatch):
    contenedor, _emb, llm, store = contenedor_prueba(enable_query_rewrite=False)
    asyncio.run(seed_documentos(store, "doc-dir-1", "rrhh", ["Son veintitrés días naturales."]))
    llm.encolar("Son veintitrés días (Fuente 1).")
    planificador = PlanificadorFalso([
        AIMessage(content="", tool_calls=[{
            "name": "buscar_rrhh",
            "args": {"query": "vacaciones"},
            "id": "call-1",
            "type": "tool_call",
        }]),
        AIMessage(content="ok"),
    ])
    monkeypatch.setattr("app.agents.director._modelo_planificador", lambda settings: planificador)

    app = crear_app(contenedor=contenedor)
    with TestClient(app) as client:
        resp = client.post(
            "/chat",
            headers=CLAVE,
            json={"question": "¿Vacaciones?", "mode": "agentic"},
        )
    assert resp.status_code == 200

    eventos, nombre = [], None
    for linea in resp.text.splitlines():
        if linea.startswith("event:"):
            nombre = linea[6:].strip()
        elif linea.startswith("data:") and nombre:
            eventos.append(nombre)

    assert eventos[0] == "meta" or eventos.count("agent") >= 3  # meta lo emite el relay .NET
    primeros_agent = eventos.index("agent")
    primer_token = eventos.index("token")
    assert primeros_agent < primer_token  # el progreso llega SIEMPRE antes del primer token

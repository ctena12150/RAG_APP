# Evidentia·RAG — Agentic RAG Assistant (ES)

RAG completo donde **cada respuesta se rastrea hasta su fuente exacta**. Un *Director de
orquesta* decide qué agente especializado (RRHH / Mantenimiento / Onboarding) consulta los
documentos; la recuperación es híbrida (pgvector + Full-Text en español) fusionada con RRF,
con reranking LLM, multi-query, auto-verificación en segundo plano y trazas etapa a etapa.

| Capa | Tecnología |
|---|---|
| Backend API | **ASP.NET Core 10** (Dapper + Npgsql, PdfPig/OpenXml, SSE relay) |
| Servicio RAG | **Python 3.12 · FastAPI · LangChain** |
| Base de datos | **PostgreSQL 16 + pgvector** — esquema `app` (.NET) y `rag` (Python, chunks+embeddings+tsvector) |
| Frontend | React + Vite + TypeScript + Tailwind (temas Ledger/Signal), cmdk, Framer Motion, Mermaid lazy |
| LLM | Groq → Mistral → Ollama local (`gpt-oss:20b`) como cadena de fallback |
| Embeddings | **Ollama `bge-m3`** (VPS o local; 1024d) · alternativa cloud gratuita: Google `text-embedding-004` (768d). Lotes ≤64, sin fallback cruzado de espacios vectoriales |

## Estructura

```
backend/    src/RAG.Domain · RAG.Infrastructure · RAG.Api · tests/RAG.Api.Tests
rag-service/ app/(core, retrieval, agents, pipeline, generation, api) · tests · eval
frontend/   src/(components, state, lib, test)
scripts/postgres/schema.sql
```

## Requisitos

- .NET SDK 10, Python 3.12 (`py -3.12`), Node ≥ 20
- Clave de [Groq](https://console.groq.com/keys) (gratuita)
- Infraestructura vía Docker en la VPS (ver sección siguiente): stack completo (Postgres **pgvector** + Ollama + los 3 servicios) o solo los datos si desarrollas en local

## Despliegue full-stack en la VPS (Docker)

Todo el sistema corre en la VPS con un único compose: Postgres+pgvector, Ollama,
rag-service (Python), API .NET y frontend (nginx que sirve el build de Vite y proxea
`/api` al backend — same-origin, sin CORS).

```bash
# en la VPS
git clone https://github.com/ctena12150/RAG_APP.git && cd RAG_APP
cp deploy/.env.example deploy/.env        # define POSTGRES_PASSWORD, INTERNAL_API_KEY y GROQ_API_KEY
cd deploy && docker compose up -d --build
docker exec rag-ollama ollama pull bge-m3 # ~1.2 GB, solo la primera vez

# verificación
curl http://localhost/api/health          # estado: ok + ragService.disponible
curl http://IP_VPS:11434/api/tags         # solo desde la propia VPS (bind 127.0.0.1)
docker exec rag-postgres psql -U ragapp -c "SELECT extname FROM pg_extension WHERE extname='vector';"
```

- Solo `web` publica puerto (`WEB_PORT`, defecto 80); el resto vive en la red interna de
  compose. Para dominio+HTTPS, pon Caddy (o nginx+certbot) delante de `web`.
- 5432/11434 quedan publicados solo en `127.0.0.1` de la VPS (túnel SSH para desarrollar
  en local contra esos datos, ver «Arranque en desarrollo»).
- El rate limiter del backend honra `X-Forwarded-For` en este despliegue
  (`RateLimit__TrustProxyHeaders=true`): cada usuario conserva su cubo tras nginx.
- El esquema `app` se crea automáticamente al primer arranque (el compose monta
  `scripts/postgres/schema.sql`); el esquema `rag` (chunks+embeddings) lo crea el propio
  rag-service.

⚠️ Cambiar de modelo de embeddings exige vaciar los documentos indexados y reingestarlos.

## Arranque en desarrollo

```bash
# 1) Infraestructura de datos: Postgres(pgvector) + Ollama en la VPS (ya desplegados
#    por el stack completo; para solo datos: cd deploy && docker compose up -d postgres ollama)
#    y túnel SSH desde local: ssh -L 5432:localhost:5432 -L 11434:localhost:11434 <vps>

# 2) Credenciales locales: rag-service/.env (copiar desde .env.example, ajusta IP_VPS
#    y password) y backend/src/RAG.Api/appsettings.json → Storage:ConnectionString
#    Rellena también GROQ_API_KEY.

# 3) Servicio RAG (puerto 8000)
cd rag-service
py -3.12 -m venv .venv && .venv/Scripts/pip install -r requirements.txt
.venv/Scripts/python -m uvicorn app.main:app --port 8000
# para debuguear
.venv\Scripts\python -m pip install debugpy
.venv\Scripts\python -m debugpy --listen 5678 -m uvicorn app.main:app --port 8000

# 4) Backend .NET (puerto 5000)
cd backend/src/RAG.Api
dotnet run --urls http://localhost:5000

# 5) Frontend (puerto 5173, proxy /api → 5000)
cd frontend
npm install && npm run dev
```

> **Sin Postgres instalado**: arranca el rag-service con `RAG_STORE=memory` y el backend
> con `"Storage": { "Provider": "InMemory" }` — todo funciona con stores en memoria
> (walking skeleton); los datos no persisten.

## Modos del pipeline

- `ENABLE_AGENTIC_MODE=true` (por defecto): el Director ve solo metadatos y llama a los
  agentes especializados mediante herramientas controladas (`buscar_rrhh`,
  `buscar_mantenimiento`, `buscar_onboarding`, `listar_documentos`). Si la planificación
  falla antes del primer token, cae **transparentemente** al pipeline fijo.
- `mode=fixed` en la petición (o `ENABLE_AGENTIC_MODE=false`): pipeline determinista
  reescritura → expansión → híbrido → RRF → dedupe → rerank → generación → verificación.

## Pruebas (todas sin red ni claves)

```bash
dotnet test RagApp.slnx                      # 29 tests de integración API con fakes
cd rag-service && .venv/Scripts/python -m pytest tests -q   # 56 tests unitarios+rutas
cd frontend && npx vitest run                # 9 tests Vitest + RTL
```

## Evaluación (necesita servicios vivos + claves reales)

```bash
python eval/runner.py --base http://localhost:5000            # suite completa + anti-inyección
python eval/runner.py --base http://localhost:5000 --sin-ataques   # solo golden set
EVAL_MODO=fixed EVAL_OVERRIDES='{"hibrida":false}' python eval/runner.py ...   # estudio A/B de configs
```
Mide sobre un documento ficticio (13 casos): hit-rate objetivo ≥ 80 %, **MRR**,
**precision@5**, groundedness (LLM-juez) = 100 %, precisión de abstención y
**resistencia a inyección** (8 ataques, objetivo ≥ 87,5 %). Escribe `eval/last-report.json`.
`overridesRetrieval` acepta claves whitelisted: `hibrida`, `rerank`, `expansion`,
`reescritura`, `dedupe`.

## Rendimiento

- **Caché semántica de consultas** (`app/core/cache.py`): la primera pregunta de una
  conversación se cachea si su embedding coincide (coseno ≥ 0.95) con una previa; el turno
  completo se re-sirve sin retrieval ni LLM. LRU 128 entradas · TTL 60 min. Se invalida al
  ingestar o borrar documentos. Toggles: `ENABLE_CACHE_CONSULTAS`, `CACHE_*`.
- **Warm-up**: al arrancar, ping a la BD y embedding dummy para abrir conexiones
  (`ENABLE_WARMUP`; fallos solo logueados).
- **Progreso del agente en vivo**: el Director emite eventos SSE `agent`
  (`planificacion`/`buscando`/`hallazgo`) que el frontend muestra como chips hasta el
  primer token.

## Decisiones clave

- Los chunks viven SOLO en el servicio Python (esquema `rag`): citas llegan por SSE;
  borrar un documento limpia vectores vía llamada best-effort al servicio.
- El Director jamás recibe contenido de chunks, solo metadatos (`metadatos_documentos`).
- Citas validadas contra chunks realmente recuperados; las inventadas se eliminan del texto.
- Respuesta exacta «No dispongo de esa documentación» cuando el contexto no sustenta.
- Embeddings: proveedor fijado por despliegue (`EMBEDDINGS_CHAIN[0]`) — cambiarlo exige
  reindexar (dimensiones/espacios incompatibles).

## Guardrails (ajuste de la información)

**Salida — `rag-service`** (`app/generation/guardrails.py`, orden barato→caro):

1. **Umbral de relevancia** — si la confianza normalizada del mejor hit
   (score RRF / máximo teórico de las listas) queda por debajo de
   `GUARDRAIL_UMBRAL_RELEVANCIA` (def. 0.25, ≤0 lo desactiva), se responde con la
   frase de abstención **sin gastar generación**. Traza: etapa `guardrail_umbral`.
2. **Verificador determinista de datos** — extrae cifras/unidades/horas de la respuesta
   (formatos españoles: `27 días`, `7,5 bar`, `400 horas`, `198 V`, `9:30`) y comprueba que
   existan en los textos de las fuentes. Un dato sin soporte ⇒ veredicto `unsupported`
   con crítica precisa ⇒ dispara la revisión sugerida. Traza: etapa `guardrails`.
3. **Exigencia de citas** — con fuentes disponibles, una respuesta sin ninguna cita válida
   (y que no sea abstención) sigue el mismo camino de revisión.

Estos tres corren ANTES del juez LLM; cualquier fallo entra por el flujo no bloqueante
de revisión ya existente. Toggles: `GUARDRAIL_VERIFICAR_DATOS`, `GUARDRAIL_EXIGIR_CITAS`.

**Entrada — backend .NET**:

- Rate limiting por IP (ventana fija): 300 req/15 min general, 60 req/15 min en rutas
  caras (upload, `/api/query`, mensajes) → 429 controlado. Config en `RateLimit:*`;
  `/health` exento.
- Validación de pregunta: máx. 2000 caracteres y heurística anti-jailbreak
  («ignora las instrucciones», «revela tu prompt», «developer mode»…) → errores
  controlados `pregunta_demasiado_larga` / `consulta_no_permitida`.

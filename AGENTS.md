# AGENTS.md — Guía para agentes de código

Proyecto: **Evidentia·RAG** — RAG agéntico de 3 servicios (español: UI, prompts, tests).
Stack: ASP.NET Core 10 + Dapper/Npgsql · Python 3.12 FastAPI/LangChain · React/Vite/TS/Tailwind · PostgreSQL+pgvector.

## Mapa rápido

```
backend/src/RAG.Domain          modelos POCO + interfaces (sin dependencias)
backend/src/RAG.Infrastructure  Dapper (stores InMemory|PostgreSql), extractores pdf/docx/txt/md, RagHttpClient (SSE)
backend/src/RAG.Api             Program.cs + Endpoints/* + Middleware/* + Services/IngestionWorker|RagChatRelay
rag-service/app                 config.py · core/(llms,embeddings) · retrieval/(engine,fusion,store,memory_store)
                                chunking/chunker.py · generation/(generate,guardrails) · agents/director.py
                                pipeline/(fixed,agentic,verificacion).py · api/routes.py
frontend/src                    components/ · state/AppContext.tsx · lib/(api,types,export) · test/
scripts/postgres/schema.sql     esquema "app" (.NET); el esquema "rag" se auto-crea al arrancar Python
```

## Comandos

### Backend .NET (backend/)
```bash
dotnet build RagApp.slnx                                   # compilar todo (0 errores esperados)
dotnet test RagApp.slnx                                    # suite completa (25 tests)
dotnet test backend/tests/RAG.Api.Tests --filter "FullyQualifiedName~Upload_duplicado"   # UN test por nombre
dotnet test backend/tests/RAG.Api.Tests --filter "FullyQualifiedName~DocumentsRouteTests" # una clase
```
Los tests usan `WebApplicationFactory` con fakes (`FakeRagService`): **nunca red, nunca BD real**.

### Servicio Python (rag-service/)
```bash
cd rag-service
.venv/Scripts/python -m pytest tests -q                                    # suite completa (59)
.venv/Scripts/python -m pytest tests/test_guardrails.py -q                 # un archivo
.venv/Scripts/python -m pytest "tests/test_rutas.py::test_health_reporta_configuracion"  # UN test
```
Arrancar servicio: `.venv/Scripts/python -m uvicorn app.main:app --port 8000`
```bash
cd rag-service && .venv/Scripts/python -m ruff check app tests    # lint (debe pasar limpio)
```
Dev-deps: `pip install -r requirements-dev.txt` (ruff).

### Frontend (frontend/)
```bash
npx tsc -b                       # typecheck estricto (debe pasar sin errores)
npm run build                    # build producción
npx vitest run                   # suite completa (9)
npx vitest run src/test/lib.test.tsx                           # un archivo
npx vitest run -t "dividirPorCitas"                            # UN test por nombre
```

### Eval (requiere servicios vivos + claves reales; manual)
```bash
python rag-service/eval/runner.py --base http://localhost:5000   # escribe rag-service/eval/last-report.json
```

### Arranque en desarrollo (3 procesos)
```bash
cd rag-service && .venv/Scripts/python -m uvicorn app.main:app --port 8000   # 1) Python
dotnet run --project backend/src/RAG.Api --urls http://localhost:5000        # 2) .NET
cd frontend && npm run dev                                                   # 3) Vite :5173 (proxy /api → :5000)
```
Sin Postgres: `RAG_STORE=memory` (Python) + `"Storage:Provider": "InMemory"` (.NET).
Primer venv: `py -3.12 -m venv rag-service/.venv && ... pip install -r requirements.txt`.

## Arquitectura — invariantes que NO debes romper

- **El frontend SOLO habla con el backend .NET** (:5000); el .NET relay-a el servicio Python (:8000) por SSE.
- **Chunks y embeddings son propiedad exclusiva del esquema `rag`** (Python). El .NET no los toca; las citas llegan en el evento SSE `done`.
- **El Director ve solo metadatos** (`metadatos_documentos()`), jamás contenido de chunks. Los agentes especializados quedan acotados a su dominio vía herramientas (`buscar_rrhh|mantenimiento|onboarding`, `listar_documentos`).
- **Contrato SSE** (Python→.NET→frontend): eventos en orden `meta → agent* → token* → done → verified? → revision_available?` (+ `error`). `agent` (progreso del Director: planificacion/buscando/hallazgo) es passthrough sin persistencia y SIEMPRE antes del primer token. `done` lleva `{content, sources[], trace}`; `verified` puede traer `{revision}`.
- **Caché semántica**: solo primera pregunta de conversación (historial vacío), coseno ≥ 0.95; invalidar en `/ingest` y `DELETE /documents/{id}` (`contenedor.cache.invalidar()`); nunca cachear turnos con evento `error`.
- **Citas**: formato `(Fuente N)`; se validan contra chunks realmente recuperados; las inválidas se eliminan del texto final (`limpiar_citas_invalidas`).
- **Abstención**: frase exacta `"No dispongo de esa documentación"` cuando el contexto no sustenta (constante `FRASE_ABSTENCION`; no cambiarla sin revisar tests y guardrails).
- **Guardrails** (orden barato→caro ANTES del juez LLM): umbral de relevancia → verificador determinista de datos → exigencia de citas → juez LLM. Fallos ⇒ revisión sugerida, nunca bloquean lo ya mostrado.
- **Embeddings sin fallback cruzado entre proveedores** (espacios vectoriales incompatibles); lotes ≤64 (`embedding_batch_size`).
- Dominios fijos: `rrhh | mantenimiento | onboarding` (validar siempre con `Dominios.EsValido` / constante `DOMINIOS`).
- Errores controlados: código corto en español (`sin_documentos`, `consulta_no_permitida`…) + mensaje genérico. **Nunca filtrar detalles del proveedor LLM**.

## Convenciones .NET (backend/)

- `net10.0`; namespaces con file-scoped (`namespace RAG.Domain.Models;`).
- Capas: `RAG.Domain` (modelos POCO + interfaces, cero dependencias) ← `RAG.Infrastructure` (Dapper, extractores, cliente HTTP) ← `RAG.Api` (minimal APIs agrupadas en `Endpoints/*.MapXxx`, middleware, servicios).
- Modelos de dominio: clases `sealed` con propiedades PascalCase en español del dominio (`Documento.NombreArchivo`, `Conversation.TituloAutomatico`). DTOs/API: records posicionales.
- JSON: siempre `System.Text.Json` con `JsonSerializerDefaults.Web` (camelCase); usar `RagJson.Options` de Infrastructure.
- Errores: lanzar `ControlledException(codigo, status, mensaje)` o `KeyNotFoundException` (→404); el mapeo HTTP vive solo en `ErrorHandlingMiddleware`. No capturar-exponer excepciones crudas.
- Extractores que no obtienen texto → `ExtraccionInvalidaException` (→409 `extraccion_invalida`); NUNCA usar `InvalidOperationException` para control de flujo (cae en 500).
- Cliente hacia el RAG service: SIEMPRE vía `AddHttpClient<IRagService, RagHttpClient>` (factory); no crear `new HttpClient()`. En biblioteca (`Infrastructure/`), `ConfigureAwait(false)`.
- Duraciones DI: `RagChatRelay` es Scoped (consume stores Scoped con PostgreSql); los stores InMemory y `TextExtractorResolver` son Singleton; workers resuelven con `IServiceScopeFactory`.
- Opciones complejas inyectadas en minimal APIs requieren `[FromServices]` Y la instancia registrada en DI (ver Program.cs).
- Tests xUnit: nombres en español con guiones bajos (`Upload_dominio_invalido_devuelve_400`), `IClassFixture<ApiTestFactory>`, asserts sobre JSON con `JsonElement.GetProperty`.

## Convenciones Python (rag-service/)

- Python 3.12; `from __future__ import annotations` en todos los módulos; type hints completos.
- Docstrings en español al inicio de módulo/clases clave; comentarios solo para decisiones no evidentes.
- Dataclasses `slots=True` para modelos (`Hit`, `FuenteCard`, `Traza`); pydantic BaseModel solo para schemas de API.
- Config: TODO por entorno vía `app/config.py` (pydantic-settings); añadir interruptor = campo nuevo + valor en `.env.example`. Nunca hardcodear claves ni URLs.
- Errores: subclases de `RagError` con atributo `codigo`; traducir fallos de proveedor con `traducir_excepcion_proveedor()` (mensaje genérico). En generadores SSE, capturar al final y emitir evento `error`.
- Async: asyncpg/httpx nativos; no bloquear el event loop; límites con `asyncio.Semaphore`.
- Almacén: protocolo `RagStore` en `retrieval/base.py` (estructural; Postgres y Memory lo cumplen sin herencia). Acceder al store SOLO vía métodos públicos del motor: `engine.hay_documentos_listos()` / `engine.metadatos_documentos()` — nunca `engine._store`.
- Lint: `ruff check app tests` limpio antes de terminar (E,F,I,W; E501 desactivado — la prosa de prompts en español no se parte mecánicamente).
- Tests: pytest con fixtures en `tests/doubles.py` (`contenedor_prueba`, `seed_documentos`, fakes de embeddings/LLM deterministas). Sin `respx`: los clientes falsos se inyectan vía `crear_app(contenedor=...)`. pytest.ini ya tiene `asyncio_mode=auto` y `pythonpath=.`.

## Convenciones TypeScript (frontend/)

- Strict on; componentes función con export default; archivos PascalCase en `components/`, camelCase en `lib/` y `state/`.
- Estado global en un único `AppContext` (`state/AppContext.tsx`) con `useMemo` para el value; NO introducir otra librería de estado.
- Tipos compartidos en `lib/types.ts` (interfaces espejo de los DTOs camelCase del backend).
- Fetch/SSE centralizados en `lib/api.ts` (`api.*`, `streamChat`); parseo de citas con `dividirPorCitas`; export MD con `lib/export.ts`. Componentes no llaman a `fetch` directo.
- Estilos: tokens CSS variables (`--accent-a/b`, `--bg-elev`, `--line`…) definidos en `index.css` por tema `[data-view][data-theme]`; Tailwind utility classes encima. Clases semánticas existentes: `citation-stamp`, `wave-bar`, `prose-rag`, `btn-accent-a`, `theme-ink-soft`.
- Animaciones Framer Motion respetando `prefers-reduced-motion` (ya cubierto en CSS; no duplicar).
- Mermaid SIEMPRE lazy (`lazy(() => import("./MermaidDiagrama"))`) con fallback a `<pre>`.
- Tests Vitest+RTL en `src/test/`: mock de `state/AppContext` con `vi.mock`; textos accesibles en español.

## Al añadir funcionalidad

1. Contrato primero: tipos TS ↔ DTO .NET ↔ pydantic deben quedar espejados (camelCase en el wire).
2. Toda nueva ruta .NET pasa por validación de entrada + errores controlados; si es cara (LLM/embeddings), añadirla a `EsRutaCara` del rate limiter.
3. Todo cambio de pipeline/guardrails lleva: test unitario con fakes + toggle en config + traza si afecta al inspector.
4. Tras terminar: `dotnet build RagApp.slnx` + las 3 suites en verde antes de dar por hecho el trabajo.

## Flujos clave (dónde enganchar cambios)

**Ingesta**: `POST /api/documents/upload` → valida (formato/dominio/hash) → guarda binario en memoria → cola (`IngestionQueue`) → `IngestionWorker`: extrae texto (`TextExtractorResolver`) → `POST /ingest` Python → Python chunkea (`chunker.py`) + embeddea + persiste en pgvector → .NET marca `listo`. Estados: `pendiente|procesando|listo|error`.

**Chat**: frontend → `POST /api/conversations/{id}/messages` → valida pregunta (jailbreak/longitud) + rechazo sin documentos → persiste mensaje usuario → auto-título (truncado) → relay SSE de Python (`RagChatRelay`) → persiste assistant en `done`, verificación/revisión en sus eventos → PATCH `.../revision` aplica la revisión aceptada.

**Retrieval compartido** (`run_retrieval`): reescritura (si historial) → expansión multi-query → híbrido por variante (vector+keyword) → RRF → dedupe Jaccard → rerank LLM (fail-open) → red de rescate para preguntas amplias. Modo fijo = 1 pasada; agéntico = N pasadas por herramientas del Director + fallback transparente al fijo si el planner falla antes del primer token.

## Trampas conocidas (muerde una vez, aprende para siempre)

- Minimal APIs: un parámetro de opciones (`UploadsOptions`…) se infiere como BODY salvo que lleve `[FromServices]` y la instancia esté en DI (`AddSingleton(sp => IOptions<T>.Value)`).
- `InvalidDataException` (multipart malformado) está mapeada a 400 en `ErrorHandlingMiddleware`; no la dejes escapar como 500.
- `dotnet run` usa el puerto de `launchSettings.json` (ignora `ASPNETCORE_URLS`); pasar `--urls http://localhost:5000` explícito.
- El LSP puede mostrar errores fantasma de paquetes recién añadidos: verificar SIEMPRE con `dotnet build`/`pytest` reales antes de "arreglar".
- Windows/bash: usar rutas con `/` (las `\` se comen en cmd) y `py -3.12` para el venv.
- Los tests .NET comparten `ApiTestFactory` por clase: no asserts de "una sola llamada" globales sobre `FakeRagService` entre tests; filtrar por documento.
- En pytest, `contenedor_prueba()` desactiva verificación/expansión/rerank por defecto; activar explícitamente lo que el test ejercite.
- La confianza RRF normalizada rara vez baja de 0.5 con un solo hit: para probar el guardrail de umbral, subir `guardrail_umbral_relevancia` en el test.
- ErrorBoundary de Three.js (`SceneErrorBoundary`): su `render()` debe devolver `fallback`/`null` cuando `hasError=true`, nunca `children` (si no, el hijo roto se renderiza de nuevo causando crash en bucle). `main.tsx` debe setear `data-view` además de `data-theme` antes del primer render para evitar flash de CSS variables.

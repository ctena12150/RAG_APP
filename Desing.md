## Rol y Mision
Eres el agente experto en ingeniería del software, desarrollo web, y análisis e implemantación de RAGs.
## Contexto y Objetivo
El Objetivo es construir un RAG desde cero, capa a capa, de forma de quien lea el proyecto entienda el papel de cada componente y como evolucionarlo de prototipo a producción sin ronper el resto.
El resultado de ser un sistema real y funcional:
1. El backend sebe ser construido as Asp.net core 10 y lo exponga por API Rest  pero el RAG debe estar en un  servicio en Python que construya el RAG.

2. Web Visual (dashboard) que consuma esa API.
El RAG tiene dos fases:
1 Indexación: cargar PDF-> extraer contenido-retrieval top-k ->generación concontexto -> respuesta con citas.
# reglas obligatorias
. Los tests automatizados no pueden llamar al servicio real: usa dobles, fakes o mocks inyectables
. Evita llamadas redundantes y procesa embeddings en lotes razonables de un máximo de 64 textos
. Traduce los fallos del proveedor a errores controlados, sin filtrar datos sensible
## Stack y decisiones tecnológicas fijadas


| Capa         | Tecnología                                                                           |
| ------------ | ------------------------------------------------------------------------------------ |
| Backend      | Asp.net core 10, Python 3.12 servicio de RAG                                         |
| Frontend     | React + Vite + Typescript + tailwind                                                 |

Most RAG tutorials stop at "embed chunks, do a vector search, stuff into a prompt." That approach has a well-known failure mode: pure vector similarity misses exact matches (product names, numbers, specific phrases), there's no check on whether the retrieved chunks actually answer the question, and the pipeline runs the same fixed sequence whether the question needs one search or five. This project addresses all three directly:

Agentic retrieval — a tool-calling planner decides for itself whether a question needs searching the documents at all, and how many times, instead of a fixed pipeline that always runs exactly one search. A comparison question gets one focused search per thing being compared; a greeting gets none; a question that needs refining gets a follow-up search - all decided by the model, not hardcoded. Falls back to the deterministic fixed pipeline automatically if planning itself fails (see Architecture)
Hybrid retrieval — vector search (Pinecone) and keyword search (Postgres full-text) run in parallel and get fused with Reciprocal Rank Fusion, so a chunk that's a strong match on either signal surfaces correctly
Multi-query retrieval — the query is expanded into a couple of alternate phrasings, each searched independently and fused together with everything else, so wording mismatches between the question and the document's vocabulary don't silently lose recall
LLM reranking — a single batched call re-judges the fused, deduplicated candidates for genuine relevance, not just similarity score, before anything reaches the answering model
Query rewriting — follow-up questions ("what about the second one?") get expanded into standalone queries using conversation history before retrieval runs (fixed pipeline only - the agentic planner already sees conversation history directly and resolves references itself when it writes a search query)
Self-verification — after an answer is generated, a separate check asks whether it's actually supported by the cited sources. If not, one corrected revision streams in as a visible replacement, with the specific problem fed back into the prompt - and in agentic mode, a small follow-up search guided by that same critique, not just a reworded retry
Verifiable citations — every claim in an answer links back to the exact source chunk, with the model's own citation graph reflected in the UI (used vs. merely-retrieved sources are shown separately)
Pipeline observability — every answer carries a stage-by-stage trace, inspectable per-message in the UI, showing exactly what the pipeline (or the agent) actually did to produce it

Comportamiento funcinal
Al final el RAG constará de diferentes apartados como documentos de Recursos humanos, manuales de mantenimeinto y paraa cada tipo de documentaión deberia de haber un agente especializado que solo responda a ese tipo de documentación, por lo tanto debe haber un agente director de orquesta que decida que agente llamar para responder a la pregunta del usuario, este director de orquesta no debe tener acceso a la información de los documentos, solo debe tener acceso a los metadatos de los documentos.
## Architecture

By default (ENABLE_AGENTIC_MODE=true), a tool-calling planner decides retrieval  wich agent can search in different data sources and documents and return results for answer the question, it can decide to call the same agent multiple times or different agents for the same question or  dynamically instead of running a fixed sequence:

If planning itself fails before anything is returned to the client, the request transparently falls back to the deterministic fixed pipeline below for that one query - it never surfaces an error to the person asking. Set ENABLE_AGENTIC_MODE=false to always use the fixed pipeline:


Both paths share the same retrieval engine (runRetrieval s: expand → hybrid search → fuse → dedupe → rerank) - the agentic planner just calls it once per search it decides to make, while the fixed pipeline always calls it exactly once. Both also share the same generation and self-verification code; only how the source chunks were gathered differs.

Key Features
Retrieval & answering

Agentic retrieval planning — a tool-calling model decides whether a question needs searching at all, and how many times, via two read-only tools (search_documents, list_documents) rather than a fixed one-search-per-question sequence. A multi-part comparison question gets one focused search per part; small talk gets none, without ever letting the model answer content questions from its own parametric knowledge 
Structure-aware document chunking (markdown-header-aware, word-window fallback for plain text/PDF), with chunk boundaries snapped to the nearest sentence ending instead of cutting strictly mid-sentence
Multi-query retrieval — the query is expanded into alternate phrasings that each run hybrid search independently, all fused together with RRF (which fuses N ranked lists, not just two), so wording that doesn't match the document's exact vocabulary still has other angles to land on
Near-duplicate chunk removal before reranking (cheap word-overlap check, no extra API calls) — keeps the reranker's limited candidate budget from being spent on repeat passages, which multi-query retrieval makes more likely, and runs again to merge results across multiple agentic search calls
Adaptive top-K — broad questions ("summarize", "compare X and Y", "give me an overview") automatically pull more source chunks than narrow factual ones, via a zero-latency keyword heuristic
Hybrid search fused with RRF, LLM reranking with a rescue safety net for broad/overview questions
Self-verification runs as a background check, never a blocking one — the first answer is shown as final immediately, and a check afterward either silently confirms it or offers a corrected version as a dismissible suggestion (never an automatic rewrite of something already on screen). In agentic mode, a failed check also triggers one small follow-up search guided by the specific critique, not just a reworded retry
Generation prompt tuned for per-claim citation density and cross-source synthesis, not just source-by-source restatement
Streaming responses via Server-Sent Events — answers appear token-by-token
Cross-family model fallback (generation/utility calls automatically retry on a different model family if the primary is decommissioned - see modelFallback.js), plus a separate provider-level fallback (Mistral) if Groq itself is unreachable, not just a single model. Agentic planning failures fall back to the deterministic fixed pipeline automatically, transparent to the person asking
A golden-set evaluation harness (npm run eval) scoring retrieval precision, answer faithfulness (LLM-as-judge), and abstention accuracy against a fictional document designed so the model can't cheat with training-data knowledge - runs unchanged against either retrieval mode, since it talks to the HTTP API, not the internals
Pipeline observability — every answer carries a stage-by-stage trace built from data the pipeline already produces, at no extra API cost. Fixed-pipeline traces show rewrite → expansion → retrieval → dedup → rerank → generation → verification; agentic traces show the planner's actual tool calls (which queries it chose, how many passages each found) in place of the fixed sequence. Inspectable per-message in the UI (see below), not just in server logs
Conversations

 with auto-titled threads
Per-conversation document scoping — pick exactly which uploaded document(s) a thread searches
Export any conversation to a clean, portable Markdown file (with citations and verification notes preserved)
Filter conversations by title (search box appears once there are enough to be worth filtering)
Documents

Organize documents into folders, or leave them uncategorized — folders are a pure organizational layer, deleting one never deletes the documents inside it
Filter documents by filename or by folder
Answer formatting

The generation prompt actively asks for structure that matches the content's shape — markdown tables for comparisons/structured data, bulleted lists for enumerable items, numbered lists only when order matters, bold for scannable key terms, headers for genuinely multi-part answers, code blocks for code/commands/config — and explicitly avoids forcing structure onto a simple one-fact answer that's better as a sentence
A lightweight regex hint (formatHint in llm.js) nudges toward the single most likely structure for comparison-, steps-, and list-shaped questions specifically, at zero extra latency or cost, layered on top of the model's own judgment rather than replacing it
Sparingly, for content that describes an actual process, sequence, or architecture, the model can emit a Mermaid diagram using a fenced mermaid code block, rendered client-side as a real diagram — lazy-loaded so its bundle cost is paid only by conversations that actually contain one, with a plain-code-block fallback if a diagram ever fails to render rather than breaking the message
All of this renders through the same citation-aware pipeline as plain prose — a "(Source 1)" citation works identically inside a table cell, list item, or paragraph
Citations

Inline citation badges that expand into source cards (excerpt, full text, relevance score, chunk index)
Distinguishes sources the model actually cited from ones merely retrieved
A "Pipeline Trace" inspector on every answer — a vertical timeline of every stage the pipeline ran, with per-stage timing, a proportional duration bar, and stage-specific detail (the rewritten query, generated variants, retrieval hit counts, which candidates the reranker kept vs. dropped, the self-verification verdict) — turns the architecture diagram above into something you can click into per-query instead of only reading in server logs
Frontend

Two design systems, deliberately kept separate: the landing page runs on "Ledger" — a research-ledger/evidence-desk aesthetic (cool sage paper tones, verdigris + rust-sienna dual accents, Newsreader serif + Public Sans + JetBrains Mono), full light/dark theming with zero flash on load. The main app view runs on "Signal" — a precision instrument-panel aesthetic built around the same core idea RAG retrieval itself is doing (finding signal in noisy candidates): a deep blue-black base, a phosphor-cyan/warm-amber dual accent, Instrument Serif + Hanken Grotesk, and a live waveform motif that recurs across the empty state, loading indicators, and composer. Both are built deliberately away from the cream-and-one-accent or near-black-and-acid-green look most AI-generated UIs default to. See client/src/index.css's .signal-theme block for the full token set and the reasoning behind the split.
Citation badges styled as archive stamps (a signature element tied directly to the product's actual mechanic, not decoration) with spring-physics entrance animation
Command palette (Cmd/Ctrl+K) for quick navigation and conversation search, with staggered result entrance
Ambient animated background, staggered list/message entrance, page-transition choreography throughout via Framer Motion — all respecting prefers-reduced-motion
Code-split so the landing page ships independently from the app shell (~108KB gzipped initial load vs. ~190KB before splitting)
Fully responsive, including a proper mobile drawer sidebar
Tech Stack
Layer	Technology

Database	PostgreSql como base de datos  con dapper — documents, conversations, messages, full-text search index
pgvector como base vectorial
Frontend	React, Vite, TypeScript, Tailwind CSS, Framer Motion, react-markdown + remark-gfm, Mermaid (lazy-loaded, diagrams only)
Testing	Vitest + React Testing Library (frontend), standalone Node scripts (backend)
LLM	Groq (generation + reranking/rewriting/verification), Mistral AI (optional provider-level fallback), Ollama para ejecucion local con gpt-oss:20b

Embeddings	Jina AI (jina-embeddings-v3), Ollama para ejecucion local con nomic-embed-text
Vector store	Qdrant como base vectorial


UI primitives	Radix UI, cmdk

### Importante!
Aunque el siguiente repositorio https://github.com/Zephyrex21/agentic-rag-assistant/tree/main está creado en node.js y lo puedes ver como referencia el back end para el RAG debe ser construido en .net 10 y el RAG debe estar en un servicio en Python que construya el RAG. el frontend debe consumir esa API. el Frontend debe tener las mismas funcionalidades que el frontend actual del repositorio de nodejs pero debe estar construido en React y con la tecnologia definida en la sección Tech Stack. Aunque el ejemplo no tiene un agente orquetador quiero que si que lo crees y asi poder implementar las herramientas de forma controlada. Para la orquetación puedes utilizar langchain-pyhton o semantic-kernel-python, pero prefiero langchain-pyhton porque me gusta mas como esta estructurado.

#### consulta
.Rechaza de forma controlada una consulta si todavía no hay documento.
.Recupera los chunks más relevantes por similitud coseno.
.Genera la respuesta exclusivamente con el contexto recuperado.
.El prompt del generador debe exigir citas de la página y documento y la respuesta exacta no dispongo de esa documentación cuando el contexto no sustenta la respuesta.
.Devuelve citas estructuradas con fuente, página, snippet y core, además de un booleano grounded.
. No inventes citas. Las citas devueltas deben de corresponder a chuncks realmente recuperados

## Ui esperada: dashboard de tres paneles
Implementa una interfaz usable y responsive:
guiate de la uri https://agentic-rag-assistant-seven.vercel.app/

## Orden de implementación
Trabaja de forma incremental, pero continua automáticamente hasta completar todo el proyecto
1. Inspecciona el repositorio
2. Crea la estructura del backend, sus modelos de dominio, interfaces y configuración.
3. Implementa primero walking skeleton del pipeline completo con vector store en memoria y dobles de inferencia verificables por tests
4. Añade estracción de pdf, chuncking y persistencia Chroma detrás de los mismos contratos.
5. Implemeta el backend sus tests de rutas y errores
6. Implementa el dashboard React y su integración con la API.
7. Añadir evaluación ,calidad, documentación operativa y comprobaciones finales.
Tras cada paso, ejecuta las pruebas o comprobaciones relevantes y corrige los fallos antes de continuar.

### Evaluaciones y test
. Crea eval con soporte para 8-12 casos: preguntas de texto corrido
. Implementa un runner de evaluación reproducible que mida al menos retrieval hit-rate y groundedness.
.Umbrales objetivo: hit.rate >=80% y groundedness 100 %. Para el walking skeleton se admite temporalmente hit-rate >= 60%, pero la entrega final debe apuntar al umbral general.
.Cubre con tests unitarios los contratos y reglas de dominio, con tests de integración la API usando dependencias falsas y con comprobaciones del frontend sus estados críticos.
.Los test deben ser deterministas, no depender de red y no necesitar una API key real.

## Calidad de implementación
.Usa tipos explícitos y separación clara entre dominio, infraestructura y transporte.
.Mantén las funciones pequeñas y los errores controlados.
.No introduces transacciones sin uso ni  funcionalidades fuera del alcance.
.No ocultes fallos con excepciones genéricas o valores por defecto engañosos.
.Mantén una experiencia local sencilla y comandos reproducibles.
. Añade comentarios solo cuando expliquen una decisión que el código no haga evidente por si mismo





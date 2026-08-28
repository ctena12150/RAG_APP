"""Configuración central del servicio RAG (12-factor, todo por entorno)."""

from __future__ import annotations

from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict

DOMINIOS = ("rrhh", "mantenimiento", "onboarding")


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", env_file_encoding="utf-8", extra="ignore")

    # --- almacenamiento ---
    # "postgres" (producción, pgvector) o "memory" (walking skeleton / pruebas)
    rag_store: str = "postgres"
    database_dsn: str = "postgresql://postgres:postgres@localhost:5432/ragapp"
    embedding_dim: int = 1024  # bge-m3=1024 (Ollama) | text-embedding-004=768 (Google)

    # --- embeddings ---
    embeddings_chain: str = "ollama:bge-m3"  # alternativa cloud gratuita: google:text-embedding-004
    embedding_batch_size: int = 64  # regla obligatoria: lotes de máximo 64 textos
    embedding_max_concurrency: int = 2
    embedding_max_retries: int = 3

    # --- generación y utilidades ---
    generation_chain: str = "groq:openai/gpt-oss-120b,ollama:qwen3:8b"
    utility_chain: str = "groq:openai/gpt-oss-20b,ollama:qwen3:8b"
    planner_chain: str = ""
    razonamiento: str = "off"

    groq_api_key: str = ""
    mistral_api_key: str = ""
    google_api_key: str = ""   # solo si EMBEDDINGS_CHAIN usa google (free tier de AI Studio)
    groq_base_url: str = "https://api.groq.com/openai/v1"
    mistral_base_url: str = "https://api.mistral.ai/v1"
    ollama_base_url: str = "http://localhost:11434/v1"
    llm_timeout_seconds: int = 60

    # --- interruptores del pipeline ---
    enable_agentic_mode: bool = True
    enable_hybrid_search: bool = True
    enable_query_rewrite: bool = True
    enable_query_expansion: bool = True
    query_expansion_count: int = 2
    enable_deduplication: bool = True
    dedup_similarity_threshold: float = 0.75
    enable_reranking: bool = True
    enable_adaptive_topk: bool = True
    adaptive_topk_bonus: int = 4
    retrieval_top_k: int = 6
    retrieval_candidate_pool: int = 30
    enable_self_verification: bool = True
    enable_format_hints: bool = True
    enable_pipeline_trace: bool = True

    # --- agéntico ---
    agentic_max_steps: int = 3
    enable_agentic_research_on_revision: bool = False

    background_verification_timeout_ms: int = 20000

    # --- caché semántica de consultas (solo primera pregunta, historial vacío) ---
    enable_cache_consultas: bool = True
    cache_umbral_similitud: float = 0.95
    cache_max_entradas: int = 128
    cache_ttl_minutos: int = 60

    # --- arranque ---
    enable_warmup: bool = True   # ping BD + embedding dummy al arrancar (fallos solo logueados)

    # --- guardrails (ajuste de la información) ---
    # confianza mínima de recuperación normalizada 0..1; <=0 desactiva el guardrail
    guardrail_umbral_relevancia: float = 0.25
    guardrail_verificar_datos: bool = True   # cifras/fechas de la respuesta deben existir en las fuentes
    guardrail_exigir_citas: bool = True      # con fuentes disponibles, la respuesta debe citar al menos una

    # --- seguridad ---
    internal_api_key: str = "dev-internal-key"

    def chain(self, name: str) -> list[tuple[str, str]]:
        raw = {
            "generation": self.generation_chain,
            "utility": self.utility_chain,
            "planner": self.planner_chain or self.utility_chain,
            "embeddings": self.embeddings_chain,
        }[name]
        steps: list[tuple[str, str]] = []
        for part in raw.split(","):
            provider, _, model = part.strip().partition(":")
            if provider and model:
                steps.append((provider.lower(), model))
        return steps


@lru_cache
def get_settings() -> Settings:
    return Settings()

# Imagen del servicio RAG (FastAPI + LangChain, Python 3.12)
# Build: docker build -t ragapp/rag-service:local .
# La configuración es 100% por entorno (app/config.py); no se copia ningún .env.

FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1

WORKDIR /srv

# dependencias primero: capa cacheable mientras no cambien requirements
COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt

COPY app ./app

RUN useradd --create-home --uid 10001 raguser \
    && chown -R raguser:raguser /srv
USER raguser

EXPOSE 8000
CMD ["python", "-m", "uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]

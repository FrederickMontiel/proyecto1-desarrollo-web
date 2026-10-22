# syntax=docker/dockerfile:1

# Herramienta de un solo uso: migra los datos de la Fase 2 a las bases de la Fase 3.
# Se ejecuta con:  docker compose --profile migracion run --rm migracion
FROM python:3.14-slim

ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
WORKDIR /migracion

RUN pip install --no-cache-dir pymssql==2.4.5 "psycopg[binary]==3.3.6" pymongo==4.19.0

COPY migrar.py ./

RUN useradd --system --uid 10001 --no-create-home migracion
USER migracion

ENTRYPOINT ["python", "migrar.py"]
CMD ["--confirmar"]

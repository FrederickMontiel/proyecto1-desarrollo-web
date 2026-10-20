# syntax=docker/dockerfile:1

# --------------------------------------------------------------------------------------
# Report Service (FastAPI + Jinja2 + WeasyPrint + MongoDB).
#
# WeasyPrint dibuja con Pango/HarfBuzz: son librerías nativas que se instalan en la
# imagen base. Las fuentes DejaVu cubren acentos, eñes y símbolos (°, µ, ³).
# --------------------------------------------------------------------------------------
FROM python:3.14-slim AS base

RUN apt-get update \
    && apt-get install --no-install-recommends --yes \
        libpango-1.0-0 libpangoft2-1.0-0 libharfbuzz-subset0 fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PATH="/venv/bin:$PATH"

# --------------------------------------------------------------------------------------
FROM base AS dependencias
RUN python -m venv /venv
COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt

# --------------------------------------------------------------------------------------
# Pruebas: aquí sí está Pango, así que también corre la que genera un PDF real. Si alguna
# falla, la imagen no se construye.
# --------------------------------------------------------------------------------------
FROM dependencias AS pruebas
WORKDIR /pruebas
COPY requirements-dev.txt pyproject.toml ./
RUN pip install --no-cache-dir pytest==9.1.1
COPY app/ app/
COPY tests/ tests/
RUN python -m pytest -q -p no:cacheprovider && touch /pruebas/aprobadas

# --------------------------------------------------------------------------------------
FROM base AS final
WORKDIR /aplicacion

COPY --from=dependencias /venv /venv
# Obliga a que la etapa de pruebas se ejecute antes de armar la imagen final.
COPY --from=pruebas /pruebas/aprobadas /tmp/pruebas-aprobadas
COPY app/ app/

# uvicorn usa uvloop, que resuelve DNS en el threadpool de libuv (4 hilos por defecto). Si
# un servicio vecino está caído, cada búsqueda de su nombre tarda ~4 s en fallar y retiene
# su hilo: con 4 así, también se bloquean las llamadas a los servicios sanos. Con 64 hilos
# una caída no arrastra a los demás.
ENV UV_THREADPOOL_SIZE=64

RUN useradd --system --uid 10001 --no-create-home reportes
USER reportes

EXPOSE 8000

HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=5 \
    CMD python -c "import urllib.request,sys; sys.exit(0 if urllib.request.urlopen('http://127.0.0.1:8000/health/live', timeout=3).status == 200 else 1)"

CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000", \
     "--proxy-headers", "--forwarded-allow-ips", "*", "--timeout-graceful-shutdown", "25", "--no-server-header"]

# syntax=docker/dockerfile:1

# --------------------------------------------------------------------------------------
# Audit Service (FastAPI + MongoDB). Las dependencias se instalan en un entorno virtual en
# una etapa aparte: la imagen final no lleva pip ni cachés de compilación.
# --------------------------------------------------------------------------------------
FROM python:3.14-slim AS build
WORKDIR /origen

RUN python -m venv /venv
ENV PATH="/venv/bin:$PATH"

COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt

FROM python:3.14-slim AS final
WORKDIR /aplicacion

ENV PATH="/venv/bin:$PATH" \
    PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

COPY --from=build /venv /venv
COPY app/ app/

# uvicorn usa uvloop, que resuelve DNS en el threadpool de libuv (4 hilos por defecto). Si
# un servicio vecino está caído, cada búsqueda de su nombre tarda ~4 s en fallar y retiene
# su hilo: con 4 así, también se bloquean las llamadas a los servicios sanos. Con 64 hilos
# una caída no arrastra a los demás.
ENV UV_THREADPOOL_SIZE=64

# Usuario sin privilegios: el proceso no necesita escribir en disco.
RUN useradd --system --uid 10001 --no-create-home auditoria
USER auditoria

EXPOSE 8000

HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=5 \
    CMD python -c "import urllib.request,sys; sys.exit(0 if urllib.request.urlopen('http://127.0.0.1:8000/health/live', timeout=3).status == 200 else 1)"

# --proxy-headers: la IP real del cliente llega en X-Forwarded-For desde el gateway.
# --timeout-graceful-shutdown: al recibir SIGTERM deja terminar las peticiones en curso.
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000", \
     "--proxy-headers", "--forwarded-allow-ips", "*", "--timeout-graceful-shutdown", "20", "--no-server-header"]

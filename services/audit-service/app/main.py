"""Punto de entrada del Audit Service."""

import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from pymongo import AsyncMongoClient
from starlette.exceptions import HTTPException as StarletteHTTPException

from .configuracion import obtener_configuracion
from .repositorio import COLECCION, RepositorioMongo
from .rutas import interno, publico
from .seguridad import ValidadorJwt

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
logger = logging.getLogger("audit-service")


@asynccontextmanager
async def ciclo_de_vida(app: FastAPI):
    config = obtener_configuracion()

    cliente = AsyncMongoClient(config.mongo_uri, serverSelectionTimeoutMS=5000, tz_aware=True)
    repositorio = RepositorioMongo(cliente[config.mongo_base][COLECCION])
    await repositorio.crear_indices()

    app.state.mongo = cliente
    app.state.repositorio = repositorio
    app.state.validador = ValidadorJwt(config)
    logger.info("Audit Service listo; base %s.", config.mongo_base)

    yield

    # Uvicorn llega aquí al recibir SIGTERM, después de terminar las peticiones en curso.
    await app.state.validador.cerrar()
    await cliente.close()
    logger.info("Audit Service detenido.")


def crear_app(ciclo=ciclo_de_vida) -> FastAPI:
    app = FastAPI(
        title="SWMATRC — Audit Service",
        description="Bitácora centralizada de las acciones de todos los microservicios.",
        version="1.0.0",
        lifespan=ciclo,
        # La documentación interactiva solo se sirve dentro de la red interna, no por el gateway.
        docs_url="/internal/docs",
        openapi_url="/internal/openapi.json",
        redoc_url=None,
    )
    app.include_router(publico)
    app.include_router(interno)
    _registrar_errores(app)
    _registrar_salud(app)
    return app


# --- Errores como ProblemDetails, el mismo formato que el gateway y los demás servicios ----

TITULOS = {
    400: "Solicitud inválida",
    401: "No autenticado",
    403: "Acceso denegado",
    404: "Recurso no encontrado",
    405: "Método no permitido",
    422: "Solicitud inválida",
}

MENSAJES_VALIDACION = {
    "missing": "es obligatorio",
    "extra_forbidden": "no está permitido",
    "string_too_short": "es demasiado corto",
    "string_too_long": "es demasiado largo",
    "int_parsing": "debe ser un número entero",
    "datetime_from_date_parsing": "debe ser una fecha válida",
    "datetime_parsing": "debe ser una fecha válida",
    "greater_than_equal": "es menor que el mínimo permitido",
    "too_short": "no puede estar vacío",
    "too_long": "tiene demasiados elementos",
}


def _problema(estado: int, detalle: str, request: Request) -> JSONResponse:
    return JSONResponse(
        status_code=estado,
        media_type="application/problem+json",
        content={
            "type": f"https://httpstatuses.io/{estado}",
            "title": TITULOS.get(estado, "Error interno" if estado >= 500 else "Error"),
            "status": estado,
            "detail": detalle,
            "instance": request.url.path,
        },
    )


def _registrar_errores(app: FastAPI) -> None:
    @app.exception_handler(StarletteHTTPException)
    async def http(request: Request, error: StarletteHTTPException):
        detalle = error.detail if isinstance(error.detail, str) else "La solicitud no pudo procesarse."
        if error.status_code == 404 and detalle == "Not Found":
            detalle = "La ruta solicitada no existe."
        return _problema(error.status_code, detalle, request)

    @app.exception_handler(RequestValidationError)
    async def validacion(request: Request, error: RequestValidationError):
        mensajes = []
        for e in error.errors():
            campo = ".".join(str(parte) for parte in e["loc"] if parte not in ("body", "query"))
            mensajes.append(f"El campo '{campo}' {MENSAJES_VALIDACION.get(e['type'], 'no es válido')}.")
        return _problema(400, " ".join(mensajes), request)

    @app.exception_handler(Exception)
    async def inesperado(request: Request, error: Exception):
        logger.exception("Error no controlado en %s %s", request.method, request.url.path)
        return _problema(500, "Ocurrió un error inesperado al procesar la solicitud.", request)


# --- Sondas para Docker y Kubernetes -------------------------------------------------------


def _registrar_salud(app: FastAPI) -> None:
    @app.get("/health/live", include_in_schema=False)
    async def vivo():
        return {"status": "ok"}

    @app.get("/health/ready", include_in_schema=False)
    async def listo(request: Request):
        try:
            await request.app.state.mongo.admin.command("ping")
        except Exception:
            return JSONResponse(status_code=503, content={"status": "error", "mongo": "sin conexión"})
        return {"status": "ok", "mongo": "ok"}


app = crear_app()

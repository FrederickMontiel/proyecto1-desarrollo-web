"""Punto de entrada del Report Service."""

import logging
from contextlib import asynccontextmanager

import httpx
from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from pymongo import AsyncMongoClient
from starlette.exceptions import HTTPException as StarletteHTTPException

from .clientes import ClienteAuditoria, ClienteServicios, ProveedorTokenServicio
from .configuracion import obtener_configuracion
from .repositorio import RepositorioReportes
from .rutas import rutas
from .seguridad import ValidadorJwt
from .servicio import ErrorSolicitud, NoDisponible, NoEncontrado, ServicioReportes

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
logger = logging.getLogger("report-service")


@asynccontextmanager
async def ciclo_de_vida(app: FastAPI):
    config = obtener_configuracion()

    cliente_mongo = AsyncMongoClient(config.mongo_uri, serverSelectionTimeoutMS=5000, tz_aware=True)
    repositorio = RepositorioReportes(cliente_mongo[config.mongo_base])
    await repositorio.crear_indices()
    interrumpidos = await repositorio.marcar_interrumpidos(config.minutos_reporte_interrumpido)
    if interrumpidos:
        logger.warning("%d reportes quedaron a medias en un arranque anterior y se marcaron como fallidos.", interrumpidos)

    # Un solo cliente HTTP para todo el servicio (reutiliza conexiones). Los reportes
    # grandes recorren varias páginas: el tiempo de espera es por petición, no por reporte.
    http = httpx.AsyncClient(timeout=httpx.Timeout(20.0, connect=3.0))
    tokens = ProveedorTokenServicio(config, http)
    datos = ClienteServicios(config, http, tokens)
    auditoria = ClienteAuditoria(config, http, tokens)
    auditoria.iniciar()

    app.state.mongo = cliente_mongo
    app.state.datos = datos
    app.state.validador = ValidadorJwt(config)
    app.state.servicio = ServicioReportes(config, repositorio, datos, auditoria)
    logger.info("Report Service listo; base %s.", config.mongo_base)

    yield

    # SIGTERM: se da un plazo a los reportes en curso, se vacía la auditoría y se cierra todo.
    await app.state.servicio.esperar_tareas(20)
    await auditoria.detener()
    await app.state.validador.cerrar()
    await http.aclose()
    await cliente_mongo.close()
    logger.info("Report Service detenido.")


def crear_app(ciclo=ciclo_de_vida) -> FastAPI:
    app = FastAPI(
        title="SWMATRC — Report Service",
        description="Generación de reportes PDF y su historial.",
        version="1.0.0",
        lifespan=ciclo,
        docs_url="/internal/docs",
        openapi_url="/internal/openapi.json",
        redoc_url=None,
    )
    app.include_router(rutas)
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
    409: "Conflicto",
}

MENSAJES_VALIDACION = {
    "missing": "es obligatorio",
    "extra_forbidden": "no está permitido",
    "string_too_short": "es demasiado corto",
    "string_too_long": "es demasiado largo",
    "int_parsing": "debe ser un número entero",
    "bool_parsing": "debe ser verdadero o falso",
    "datetime_from_date_parsing": "debe ser una fecha válida",
    "datetime_parsing": "debe ser una fecha válida",
    "greater_than_equal": "es menor que el mínimo permitido",
    "literal_error": "no tiene un valor permitido",
    "model_attributes_type": "debe ser un objeto",
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
    @app.exception_handler(ErrorSolicitud)
    async def solicitud(request: Request, error: ErrorSolicitud):
        return _problema(400, str(error), request)

    @app.exception_handler(NoEncontrado)
    async def no_encontrado(request: Request, error: NoEncontrado):
        return _problema(404, str(error), request)

    @app.exception_handler(NoDisponible)
    async def no_disponible(request: Request, error: NoDisponible):
        return _problema(409, str(error), request)

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
            if e["type"] == "value_error":
                # Los validadores propios ya redactan el mensaje en español.
                mensajes.append(str(e["msg"]).removeprefix("Value error, "))
                continue
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

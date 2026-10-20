"""Endpoints del Report Service."""

from datetime import datetime
from typing import Annotated, Literal

from fastapi import APIRouter, Depends, Query, Request, status
from fastapi.responses import Response
from pydantic import BaseModel, ConfigDict, Field

from . import dashboard
from .reportes import Filtros
from .repositorio import ESTADOS
from .seguridad import Identidad, requiere_permiso
from .servicio import PERMISO_DESCARGAR, PERMISO_GENERAR, Contexto, ServicioReportes

rutas = APIRouter(prefix="/api/reports", tags=["reportes"])


class SolicitudReporte(BaseModel):
    model_config = ConfigDict(extra="forbid")

    type: str = Field(min_length=1, max_length=40)
    filters: Filtros = Field(default_factory=Filtros)


def _servicio(request: Request) -> ServicioReportes:
    return request.app.state.servicio


def _contexto(request: Request, identidad: Identidad) -> Contexto:
    return Contexto(identidad, request.client.host if request.client else None, request.headers.get("x-correlation-id"))


ServicioDep = Annotated[ServicioReportes, Depends(_servicio)]
PuedeGenerar = Annotated[Identidad, Depends(requiere_permiso(PERMISO_GENERAR))]
PuedeDescargar = Annotated[Identidad, Depends(requiere_permiso(PERMISO_DESCARGAR))]
PuedeVerTablero = Annotated[Identidad, Depends(requiere_permiso("comunidades:leer", "sensores:leer", "alertas:leer"))]


@rutas.get("/types")
async def tipos(_: PuedeGenerar) -> list[dict]:
    """Los reportes disponibles con los filtros que admite cada uno."""
    return ServicioReportes.tipos()


@rutas.post("", status_code=status.HTTP_202_ACCEPTED)
async def solicitar(solicitud: SolicitudReporte, request: Request, servicio: ServicioDep, identidad: PuedeGenerar) -> dict:
    """
    Encola la generación y responde de inmediato con el ``reportId`` en estado ``PENDING``.
    El estado se consulta en ``GET /api/reports/{reportId}``.
    """
    return await servicio.solicitar(solicitud.type, solicitud.filters, _contexto(request, identidad))


@rutas.get("")
async def historial(
    request: Request,
    servicio: ServicioDep,
    identidad: PuedeGenerar,
    tipo: Annotated[str | None, Query(alias="type")] = None,
    estado: Annotated[Literal[ESTADOS] | None, Query(alias="status")] = None,  # type: ignore[valid-type]
    pagina: Annotated[int, Query(ge=1)] = 1,
    tamano: Annotated[int, Query(ge=1)] = 20,
) -> dict:
    """RF-REP-09: historial de reportes. Un administrador ve el de todos; los demás, el propio."""
    return await servicio.listar(_contexto(request, identidad), tipo, estado, pagina, tamano)


@rutas.get("/dashboard")
async def tablero(
    request: Request,
    _: PuedeVerTablero,
    comunidadId: Annotated[int | None, Query(ge=1)] = None,
    desde: datetime | None = None,
    hasta: datetime | None = None,
) -> dict:
    """Indicadores agregados del dashboard estadístico (sección 15)."""
    return await dashboard.indicadores(request.app.state.datos, comunidadId, desde, hasta)


@rutas.get("/{report_id}")
async def obtener(report_id: str, request: Request, servicio: ServicioDep, identidad: PuedeGenerar) -> dict:
    return await servicio.obtener(report_id, _contexto(request, identidad))


@rutas.get("/{report_id}/download")
async def descargar(report_id: str, request: Request, servicio: ServicioDep, identidad: PuedeDescargar) -> Response:
    """RF-REP-10: descarga del PDF, exclusiva de administradores. Queda registrada."""
    nombre, contenido = await servicio.descargar(report_id, _contexto(request, identidad))
    return Response(
        content=contenido,
        media_type="application/pdf",
        headers={"Content-Disposition": f'attachment; filename="{nombre}"', "Cache-Control": "no-store"},
    )

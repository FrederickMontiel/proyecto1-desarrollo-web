"""Endpoints del Audit Service."""

from datetime import datetime
from typing import Annotated

from fastapi import APIRouter, Depends, HTTPException, Query, Request, status

from .configuracion import Configuracion, obtener_configuracion
from .modelos import Catalogos, EventoEntrada, Filtro, LoteEventos, Pagina
from .repositorio import Repositorio
from .seguridad import Identidad, requiere_permiso, solo_servicios

PERMISO_AUDITORIA = "auditoria:leer"


def obtener_repositorio(request: Request) -> Repositorio:
    return request.app.state.repositorio


RepositorioDep = Annotated[Repositorio, Depends(obtener_repositorio)]

# --- Consulta (frontend, a través del gateway) -------------------------------------------

publico = APIRouter(prefix="/api/audit", tags=["auditoría"])


@publico.get("/events", response_model=Pagina)
async def listar_eventos(
    repositorio: RepositorioDep,
    config: Annotated[Configuracion, Depends(obtener_configuracion)],
    _: Annotated[Identidad, Depends(requiere_permiso(PERMISO_AUDITORIA))],
    usuario: Annotated[str | None, Query(description="Correo o parte de él")] = None,
    accion: str | None = None,
    entidad: str | None = None,
    servicio: str | None = None,
    desde: datetime | None = None,
    hasta: datetime | None = None,
    pagina: Annotated[int, Query(ge=1)] = 1,
    tamano: Annotated[int, Query(ge=1)] = 30,
) -> Pagina:
    if desde and hasta and desde > hasta:
        raise HTTPException(status.HTTP_400_BAD_REQUEST, "La fecha 'desde' no puede ser posterior a 'hasta'.")

    filtro = Filtro(
        usuario=usuario,
        accion=accion,
        entidad=entidad,
        servicio=servicio,
        desde=desde,
        hasta=hasta,
        pagina=pagina,
        # Se acota en lugar de rechazar: pedir 10 000 filas no debe tumbar el servicio.
        tamano=min(tamano, config.tamano_pagina_maximo),
    )
    return await repositorio.buscar(filtro)


@publico.get("/catalogs", response_model=Catalogos)
async def catalogos(
    repositorio: RepositorioDep,
    _: Annotated[Identidad, Depends(requiere_permiso(PERMISO_AUDITORIA))],
) -> Catalogos:
    """Valores distintos de acción, entidad y servicio, para llenar los filtros del frontend."""
    return await repositorio.catalogos()


# --- Registro (solo microservicios, dentro de la red interna) ----------------------------
# Viven fuera de /api: el gateway no las enruta.

interno = APIRouter(prefix="/internal", tags=["interno"])


@interno.post("/events", status_code=status.HTTP_202_ACCEPTED)
async def registrar_evento(
    evento: EventoEntrada,
    repositorio: RepositorioDep,
    _: Annotated[Identidad, Depends(solo_servicios)],
) -> dict[str, int]:
    return {"registrados": await repositorio.insertar([evento])}


@interno.post("/events/batch", status_code=status.HTTP_202_ACCEPTED)
async def registrar_lote(
    lote: LoteEventos,
    repositorio: RepositorioDep,
    _: Annotated[Identidad, Depends(solo_servicios)],
) -> dict[str, int]:
    return {"registrados": await repositorio.insertar(lote.eventos)}

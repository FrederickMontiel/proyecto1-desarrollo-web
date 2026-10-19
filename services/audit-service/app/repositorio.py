"""Acceso a la colección ``eventos`` de MongoDB."""

import math
import re
from datetime import datetime, timezone
from typing import Any, Protocol

from pymongo import ASCENDING, DESCENDING, IndexModel
from pymongo.asynchronous.collection import AsyncCollection

from .modelos import Catalogos, EventoEntrada, EventoSalida, Filtro, Pagina

COLECCION = "eventos"


class Repositorio(Protocol):
    async def insertar(self, eventos: list[EventoEntrada]) -> int: ...
    async def buscar(self, filtro: Filtro) -> Pagina: ...
    async def catalogos(self) -> Catalogos: ...


class RepositorioMongo:
    def __init__(self, coleccion: AsyncCollection) -> None:
        self._coleccion = coleccion

    async def crear_indices(self) -> None:
        # Las consultas de la pantalla de bitácora siempre ordenan por fecha descendente y
        # filtran por uno de estos campos: cada índice compuesto cubre filtro + orden.
        await self._coleccion.create_indexes(
            [
                IndexModel([("fechaHora", DESCENDING)], name="ix_fecha"),
                IndexModel([("accion", ASCENDING), ("fechaHora", DESCENDING)], name="ix_accion_fecha"),
                IndexModel([("entidad", ASCENDING), ("fechaHora", DESCENDING)], name="ix_entidad_fecha"),
                IndexModel([("servicio", ASCENDING), ("fechaHora", DESCENDING)], name="ix_servicio_fecha"),
                IndexModel([("usuarioEmail", ASCENDING), ("fechaHora", DESCENDING)], name="ix_usuario_fecha"),
                IndexModel([("correlacion", ASCENDING)], name="ix_correlacion", sparse=True),
            ]
        )

    async def insertar(self, eventos: list[EventoEntrada]) -> int:
        ahora = datetime.now(timezone.utc)
        documentos = [
            {**evento.model_dump(), "fechaHora": evento.fechaHora or ahora, "recibidoEn": ahora} for evento in eventos
        ]
        # ordered=False: si un documento del lote falla, los demás se insertan igual.
        resultado = await self._coleccion.insert_many(documentos, ordered=False)
        return len(resultado.inserted_ids)

    async def buscar(self, filtro: Filtro) -> Pagina:
        consulta = _consulta(filtro)
        total = await self._coleccion.count_documents(consulta)
        cursor = (
            self._coleccion.find(consulta)
            .sort([("fechaHora", DESCENDING), ("_id", DESCENDING)])
            .skip((filtro.pagina - 1) * filtro.tamano)
            .limit(filtro.tamano)
        )
        elementos = [_a_salida(documento) async for documento in cursor]

        return Pagina(
            elementos=elementos,
            pagina=filtro.pagina,
            tamanoPagina=filtro.tamano,
            totalElementos=total,
            totalPaginas=math.ceil(total / filtro.tamano) if filtro.tamano else 0,
        )

    async def catalogos(self) -> Catalogos:
        return Catalogos(
            acciones=sorted(await self._coleccion.distinct("accion")),
            entidades=sorted(await self._coleccion.distinct("entidad")),
            servicios=sorted(await self._coleccion.distinct("servicio")),
        )


def _consulta(filtro: Filtro) -> dict[str, Any]:
    consulta: dict[str, Any] = {}

    if filtro.usuario:
        # Búsqueda parcial por correo. El texto se escapa: un filtro como ".*" no debe
        # convertirse en una expresión regular que recorra toda la colección.
        consulta["usuarioEmail"] = {"$regex": re.escape(filtro.usuario.strip()), "$options": "i"}
    if filtro.accion:
        consulta["accion"] = filtro.accion
    if filtro.entidad:
        consulta["entidad"] = filtro.entidad
    if filtro.servicio:
        consulta["servicio"] = filtro.servicio

    rango: dict[str, datetime] = {}
    if filtro.desde:
        rango["$gte"] = filtro.desde
    if filtro.hasta:
        rango["$lte"] = filtro.hasta
    if rango:
        consulta["fechaHora"] = rango

    return consulta


def _a_salida(documento: dict[str, Any]) -> EventoSalida:
    fecha: datetime = documento["fechaHora"]
    return EventoSalida(
        id=str(documento["_id"]),
        servicio=documento.get("servicio", ""),
        usuarioId=documento.get("usuarioId"),
        usuarioEmail=documento.get("usuarioEmail", "sistema"),
        accion=documento["accion"],
        entidad=documento["entidad"],
        entidadId=documento.get("entidadId"),
        detalle=documento.get("detalle"),
        direccionIp=documento.get("direccionIp"),
        correlacion=documento.get("correlacion"),
        # MongoDB guarda las fechas en UTC pero las devuelve sin zona horaria.
        fechaHora=fecha if fecha.tzinfo else fecha.replace(tzinfo=timezone.utc),
    )

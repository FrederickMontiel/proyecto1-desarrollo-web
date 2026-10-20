"""
Persistencia en MongoDB (sección 18): la metadata de cada reporte en la colección
``reportes`` y el PDF en GridFS. El contenedor no guarda nada en disco, así que cualquier
réplica puede servir una descarga (servicio sin estado, sección 21).
"""

import math
from datetime import datetime, timedelta, timezone
from typing import Any

from bson import ObjectId
from gridfs import AsyncGridFSBucket
from pymongo import ASCENDING, DESCENDING, IndexModel, ReturnDocument
from pymongo.asynchronous.database import AsyncDatabase

ESTADOS = ("PENDING", "PROCESSING", "COMPLETED", "FAILED")


class RepositorioReportes:
    def __init__(self, db: AsyncDatabase) -> None:
        self._reportes = db["reportes"]
        self._contadores = db["contadores"]
        self._archivos = AsyncGridFSBucket(db, bucket_name="pdf")

    async def crear_indices(self) -> None:
        await self._reportes.create_indexes([
            IndexModel([("reportId", ASCENDING)], unique=True, name="ux_report_id"),
            IndexModel([("generatedAt", DESCENDING)], name="ix_fecha"),
            IndexModel([("generatedBy.id", ASCENDING), ("generatedAt", DESCENDING)], name="ix_usuario_fecha"),
            IndexModel([("type", ASCENDING), ("generatedAt", DESCENDING)], name="ix_tipo_fecha"),
            IndexModel([("status", ASCENDING)], name="ix_estado"),
        ])

    async def siguiente_id(self, ahora: datetime) -> str:
        """``REP-2026-000001``: correlativo por año, atómico aunque haya varias réplicas."""
        contador = await self._contadores.find_one_and_update(
            {"_id": f"reportes-{ahora.year}"},
            {"$inc": {"valor": 1}},
            upsert=True,
            return_document=ReturnDocument.AFTER,
        )
        return f"REP-{ahora.year}-{contador['valor']:06d}"

    async def insertar(self, documento: dict) -> None:
        await self._reportes.insert_one(documento)

    async def actualizar(self, report_id: str, cambios: dict, condicion: dict | None = None) -> bool:
        resultado = await self._reportes.update_one({"reportId": report_id, **(condicion or {})}, {"$set": cambios})
        return resultado.modified_count > 0

    async def obtener(self, report_id: str) -> dict | None:
        return await self._reportes.find_one({"reportId": report_id}, {"_id": 0})

    async def listar(self, filtro: dict, pagina: int, tamano: int) -> dict[str, Any]:
        total = await self._reportes.count_documents(filtro)
        cursor = (self._reportes.find(filtro, {"_id": 0, "downloads": {"$slice": -5}})
                  .sort([("generatedAt", DESCENDING)])
                  .skip((pagina - 1) * tamano)
                  .limit(tamano))
        return {
            "elementos": [d async for d in cursor],
            "pagina": pagina,
            "tamanoPagina": tamano,
            "totalElementos": total,
            "totalPaginas": math.ceil(total / tamano) if tamano else 0,
        }

    async def registrar_descarga(self, report_id: str, usuario: dict, ahora: datetime) -> None:
        await self._reportes.update_one(
            {"reportId": report_id},
            {"$push": {"downloads": {"by": usuario, "at": ahora}}, "$set": {"lastDownloadAt": ahora}, "$inc": {"downloadCount": 1}},
        )

    # --- Archivos ---------------------------------------------------------------------

    async def guardar_pdf(self, nombre: str, contenido: bytes, report_id: str) -> ObjectId:
        return await self._archivos.upload_from_stream(
            nombre, contenido, metadata={"reportId": report_id, "contentType": "application/pdf"})

    async def leer_pdf(self, archivo_id: ObjectId) -> bytes:
        flujo = await self._archivos.open_download_stream(archivo_id)
        return await flujo.read()

    # --- Mantenimiento ----------------------------------------------------------------

    async def marcar_interrumpidos(self, minutos: int) -> int:
        """
        Un reporte que sigue pendiente o en proceso tras varios minutos quedó a medias porque
        la réplica que lo generaba se detuvo: se marca como fallido para que el usuario sepa
        que debe pedirlo de nuevo.
        """
        limite = datetime.now(timezone.utc) - timedelta(minutes=minutos)
        resultado = await self._reportes.update_many(
            {"status": {"$in": ["PENDING", "PROCESSING"]}, "generatedAt": {"$lt": limite}},
            {"$set": {"status": "FAILED", "error": "La generación se interrumpió. Solicite el reporte de nuevo.",
                      "completedAt": datetime.now(timezone.utc)}},
        )
        return resultado.modified_count

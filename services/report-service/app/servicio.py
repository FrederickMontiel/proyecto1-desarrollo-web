"""
Ciclo de vida de un reporte (flujo de la sección 11):

    solicitar → PENDING → PROCESSING (obtener datos, preparar, HTML, PDF, guardar) → COMPLETED
                                                                                   ↘ FAILED

La generación es asíncrona (RNF-REP-05): la petición vuelve de inmediato con el
``reportId`` y el frontend consulta el estado. Un semáforo limita cuántos PDF se generan a
la vez en esta réplica, porque WeasyPrint consume CPU (RNF-REP-04).
"""

import asyncio
import logging
import time
from dataclasses import dataclass
from datetime import datetime, timezone

from .clientes import ClienteAuditoria, ClienteServicios, ServicioNoDisponible
from .configuracion import Configuracion
from .etiquetas import Formato
from .pdf import Renderizador
from .reportes import CATALOGO, Filtros, TipoReporte
from .repositorio import RepositorioReportes
from .seguridad import Identidad

logger = logging.getLogger("report-service")

PERMISO_GENERAR = "reportes:generar"
PERMISO_DESCARGAR = "reportes:descargar"

ETIQUETAS_ESTADO = {"PENDING": "Pendiente", "PROCESSING": "En proceso", "COMPLETED": "Completado", "FAILED": "Fallido"}


class ErrorSolicitud(Exception):
    """Solicitud inválida (400)."""


class NoEncontrado(Exception):
    """El reporte no existe o el usuario no puede verlo (404)."""


class NoDisponible(Exception):
    """El reporte existe pero todavía no se puede descargar (409)."""


@dataclass(frozen=True)
class Contexto:
    identidad: Identidad
    ip: str | None
    correlacion: str | None

    @property
    def usuario(self) -> dict:
        return {"id": self.identidad.id_usuario, "email": self.identidad.email, "name": self.identidad.nombre}

    @property
    def es_administrador(self) -> bool:
        # RF-REP-10: la descarga es exclusiva de los administradores; quien puede
        # descargar puede ver el historial de todos.
        return self.identidad.tiene(PERMISO_DESCARGAR)


class ServicioReportes:
    def __init__(self, config: Configuracion, repositorio: RepositorioReportes, datos: ClienteServicios,
                 auditoria: ClienteAuditoria, renderizador: Renderizador | None = None) -> None:
        self._config = config
        self._repo = repositorio
        self._datos = datos
        self._auditoria = auditoria
        self._formato = Formato(config.zona_horaria)
        self._render = renderizador or Renderizador(self._formato)
        self._semaforo = asyncio.Semaphore(config.generaciones_simultaneas)
        self._tareas: set[asyncio.Task] = set()

    # -------------------------------------------------------------------------------

    @staticmethod
    def tipos() -> list[dict]:
        return [{"type": t.codigo, "requirement": t.requisito, "name": t.nombre, "description": t.descripcion,
                 "filters": list(t.filtros)} for t in CATALOGO.values()]

    async def solicitar(self, tipo: str, filtros: Filtros, ctx: Contexto) -> dict:
        definicion = CATALOGO.get(tipo)
        if definicion is None:
            raise ErrorSolicitud(f"Tipo de reporte desconocido: '{tipo}'. Tipos válidos: {', '.join(CATALOGO)}.")

        no_admitidos = set(filtros.usados()) - set(definicion.filtros)
        if no_admitidos:
            raise ErrorSolicitud(f"El {definicion.nombre.lower()} no admite los filtros: {', '.join(sorted(no_admitidos))}. "
                                 f"Admite: {', '.join(definicion.filtros) or 'ninguno'}.")
        if filtros.desde and filtros.hasta and filtros.desde > filtros.hasta:
            raise ErrorSolicitud("La fecha 'desde' no puede ser posterior a 'hasta'.")

        ahora = datetime.now(timezone.utc)
        report_id = await self._repo.siguiente_id(ahora)
        documento = {
            "reportId": report_id,
            "type": tipo,
            "typeName": definicion.nombre,
            "requirement": definicion.requisito,
            "generatedBy": ctx.usuario,
            "generatedAt": ahora,
            "filters": filtros.model_dump(mode="json", exclude_none=True),
            "status": "PENDING",
            "fileName": f"{definicion.archivo}-{report_id}.pdf",
            "downloads": [],
            "downloadCount": 0,
        }
        await self._repo.insertar(documento)

        tarea = asyncio.create_task(self._generar(report_id, definicion, filtros, ctx))
        # Se guarda la referencia: sin ella el recolector podría descartar la tarea a medias.
        self._tareas.add(tarea)
        tarea.add_done_callback(self._tareas.discard)

        return self._presentar(documento)

    async def _generar(self, report_id: str, tipo: TipoReporte, filtros: Filtros, ctx: Contexto) -> None:
        async with self._semaforo:
            inicio = time.perf_counter()
            await self._repo.actualizar(report_id, {"status": "PROCESSING", "startedAt": datetime.now(timezone.utc)})
            try:
                # 1. Obtener datos y 2. prepararlos.
                documento = await tipo.generador(self._datos, filtros, self._formato, self._config.max_filas_reporte)
                # 3. HTML y 4. PDF.
                meta = await self._repo.obtener(report_id)
                meta["usuario"] = (ctx.identidad.nombre or ctx.identidad.email).replace('"', "")
                html = self._render.html(tipo, documento, meta, filtros, await self._nombres(filtros))
                contenido = await self._render.pdf(html)
                # 5. Registrar metadata.
                archivo = await self._repo.guardar_pdf(meta["fileName"], contenido, report_id)
                duracion = round((time.perf_counter() - inicio) * 1000)
                await self._repo.actualizar(report_id, {
                    "status": "COMPLETED", "fileId": archivo, "fileSize": len(contenido), "rows": documento.filas,
                    "warnings": documento.avisos, "completedAt": datetime.now(timezone.utc), "durationMs": duracion,
                })
                logger.info("Reporte %s (%s) generado: %d filas, %d KB en %d ms.",
                            report_id, tipo.codigo, documento.filas, len(contenido) // 1024, duracion)
                self._auditar("ReporteGenerado", report_id, ctx, {"tipo": tipo.codigo, "filas": documento.filas,
                                                                    "filtros": filtros.model_dump(mode="json", exclude_none=True)})
            except ServicioNoDisponible as error:
                await self._fallar(report_id, str(error), tipo, ctx)
            except Exception:
                logger.exception("Falló la generación del reporte %s.", report_id)
                await self._fallar(report_id, "Ocurrió un error inesperado al generar el reporte.", tipo, ctx)

    async def _fallar(self, report_id: str, mensaje: str, tipo: TipoReporte, ctx: Contexto) -> None:
        await self._repo.actualizar(report_id, {"status": "FAILED", "error": mensaje, "completedAt": datetime.now(timezone.utc)})
        self._auditar("ReporteFallido", report_id, ctx, {"tipo": tipo.codigo, "error": mensaje})

    async def _nombres(self, filtros: Filtros) -> dict[str, str]:
        """Nombres legibles para la ficha del PDF. Si el servicio no responde se deja el id."""
        nombres: dict[str, str] = {}
        try:
            if filtros.comunidadId:
                comunidad = next((c for c in await self._datos.comunidades() if c["id"] == filtros.comunidadId), None)
                nombres["comunidadId"] = comunidad["nombre"] if comunidad else f"#{filtros.comunidadId}"
            if filtros.sensorId:
                sensor = next((s for s in await self._datos.sensores() if s["id"] == filtros.sensorId), None)
                nombres["sensorId"] = f"{sensor['codigo']} — {sensor['nombre']}" if sensor else f"#{filtros.sensorId}"
        except ServicioNoDisponible:
            pass
        return nombres

    # -------------------------------------------------------------------------------

    async def listar(self, ctx: Contexto, tipo: str | None, estado: str | None, pagina: int, tamano: int) -> dict:
        """RF-REP-09. Un administrador ve el historial de todos; los demás, solo lo suyo."""
        filtro: dict = {}
        if not ctx.es_administrador:
            filtro["generatedBy.id"] = ctx.identidad.id_usuario
        if tipo:
            filtro["type"] = tipo
        if estado:
            filtro["status"] = estado
        resultado = await self._repo.listar(filtro, max(1, pagina), min(max(1, tamano), 100))
        resultado["elementos"] = [self._presentar(d) for d in resultado["elementos"]]
        return resultado

    async def obtener(self, report_id: str, ctx: Contexto) -> dict:
        return self._presentar(await self._cargar_visible(report_id, ctx))

    async def descargar(self, report_id: str, ctx: Contexto) -> tuple[str, bytes]:
        """RF-REP-10: exclusivo de administradores (la ruta exige ``reportes:descargar``)."""
        documento = await self._cargar_visible(report_id, ctx)
        if documento["status"] != "COMPLETED":
            raise NoDisponible(f"El reporte está {ETIQUETAS_ESTADO[documento['status']].lower()}; "
                               "solo se pueden descargar reportes completados.")
        contenido = await self._repo.leer_pdf(documento["fileId"])
        await self._repo.registrar_descarga(report_id, ctx.usuario, datetime.now(timezone.utc))
        self._auditar("ReporteDescargado", report_id, ctx, {"tipo": documento["type"], "archivo": documento["fileName"]})
        return documento["fileName"], contenido

    async def _cargar_visible(self, report_id: str, ctx: Contexto) -> dict:
        documento = await self._repo.obtener(report_id)
        # Un reporte ajeno responde igual que uno inexistente: no se revela que existe.
        if documento is None or (not ctx.es_administrador and documento["generatedBy"].get("id") != ctx.identidad.id_usuario):
            raise NoEncontrado(f"No se encontró el reporte {report_id}.")
        return documento

    # -------------------------------------------------------------------------------

    async def esperar_tareas(self, segundos: float) -> None:
        """Apagado ordenado: se da un plazo a los reportes en curso antes de salir."""
        if self._tareas:
            logger.info("Esperando %d reportes en curso antes de detenerse.", len(self._tareas))
            await asyncio.wait(self._tareas, timeout=segundos)

    def _auditar(self, accion: str, report_id: str, ctx: Contexto, detalle: dict) -> None:
        self._auditoria.registrar(accion, "Reporte", report_id, detalle, ctx.identidad.id_usuario,
                                  ctx.identidad.email, ctx.ip, ctx.correlacion)

    @staticmethod
    def _presentar(d: dict) -> dict:
        salida = {k: v for k, v in d.items() if k not in ("_id", "fileId")}
        salida["statusLabel"] = ETIQUETAS_ESTADO.get(d["status"], d["status"])
        return salida

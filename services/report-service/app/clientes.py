"""
Clientes de los demás microservicios. El Report Service nunca toca sus bases de datos
(sección 16 del documento): todo lo obtiene de sus API internas con un token de servicio.
"""

import asyncio
import logging
import time
from collections.abc import AsyncIterator
from datetime import datetime, timezone
from typing import Any

import httpx

from .configuracion import Configuracion

logger = logging.getLogger("report-service.clientes")


class ServicioNoDisponible(Exception):
    """Otro microservicio no respondió; el reporte se marca como fallido con este mensaje."""


class ProveedorTokenServicio:
    """Pide al Auth Service el token interno y lo reutiliza hasta un minuto antes de que venza."""

    def __init__(self, config: Configuracion, http: httpx.AsyncClient) -> None:
        self._config = config
        self._http = http
        self._token: str | None = None
        self._expira = 0.0
        self._candado = asyncio.Lock()

    async def obtener(self) -> str:
        if self._token and self._expira - time.time() > 60:
            return self._token
        async with self._candado:
            if self._token and self._expira - time.time() > 60:
                return self._token
            respuesta = await self._http.post(
                f"{self._config.url_auth}/internal/token",
                json={"clienteId": self._config.servicio_nombre, "secreto": self._config.servicio_secreto},
            )
            respuesta.raise_for_status()
            cuerpo = respuesta.json()
            self._token = cuerpo["token"]
            self._expira = datetime.fromisoformat(cuerpo["expira"].replace("Z", "+00:00")).timestamp()
            return self._token


class ClienteServicios:
    """Fuente de datos de todos los reportes."""

    def __init__(self, config: Configuracion, http: httpx.AsyncClient, tokens: ProveedorTokenServicio) -> None:
        self._config = config
        self._http = http
        self._tokens = tokens

    async def _get(self, servicio: str, url: str, params: dict[str, Any] | None = None) -> Any:
        limpios = {k: _valor(v) for k, v in (params or {}).items() if v not in (None, "")}
        try:
            respuesta = await self._http.get(
                url, params=limpios, headers={"authorization": f"Bearer {await self._tokens.obtener()}"}
            )
            respuesta.raise_for_status()
            return respuesta.json()
        except httpx.HTTPError as error:
            logger.warning("%s no respondió %s: %s %s", servicio, url, type(error).__name__, error)
            raise ServicioNoDisponible(f"{servicio} no respondió; intente de nuevo en unos minutos.") from error

    async def _paginas(self, servicio: str, url: str, filtros: dict[str, Any], limite: int) -> tuple[list[dict], int]:
        """
        Recorre un listado paginado por lotes (RNF-REP-05) hasta el límite de filas del
        reporte. Devuelve las filas y el total real, para avisar si quedaron filas fuera.
        """
        filas: list[dict] = []
        pagina, total = 1, 0
        while len(filas) < limite:
            datos = await self._get(servicio, url, {**filtros, "pagina": pagina, "tamano": self._config.tamano_lote})
            total = datos["totalElementos"]
            filas.extend(datos["elementos"])
            if pagina >= datos["totalPaginas"] or not datos["elementos"]:
                break
            pagina += 1
        return filas[:limite], total

    # --- Community Service -------------------------------------------------------------

    async def comunidades(self, activa: bool | None = None) -> list[dict]:
        return await self._get("Community Service", f"{self._config.url_community}/internal/communities", {"activa": activa})

    # --- Sensor Service ----------------------------------------------------------------

    async def sensores(self, **filtros: Any) -> list[dict]:
        return await self._get("Sensor Service", f"{self._config.url_sensor}/internal/sensors", filtros)

    async def conteo_sensores(self) -> dict[int, dict]:
        datos = await self._get("Sensor Service", f"{self._config.url_sensor}/internal/sensors/count-by-community")
        return {c["comunidadId"]: c for c in datos}

    async def lecturas(self, limite: int, **filtros: Any) -> tuple[list[dict], int]:
        return await self._paginas("Sensor Service", f"{self._config.url_sensor}/internal/readings", filtros, limite)

    async def estadisticas_lecturas(self, **filtros: Any) -> list[dict]:
        return await self._get("Sensor Service", f"{self._config.url_sensor}/internal/readings/stats", filtros)

    # --- Alert Service -----------------------------------------------------------------

    async def alertas(self, limite: int, **filtros: Any) -> tuple[list[dict], int]:
        return await self._paginas("Alert Service", f"{self._config.url_alert}/internal/alerts", filtros, limite)

    async def eventos(self, limite: int, **filtros: Any) -> tuple[list[dict], int]:
        return await self._paginas("Alert Service", f"{self._config.url_alert}/internal/events", filtros, limite)

    async def estadisticas_eventos(self, **filtros: Any) -> dict:
        return await self._get("Alert Service", f"{self._config.url_alert}/internal/events/stats", filtros)

    async def resumen_alertas(self, **filtros: Any) -> dict:
        return await self._get("Alert Service", f"{self._config.url_alert}/internal/alerts/summary", filtros)

    async def alertas_activas(self, comunidad_id: int | None = None) -> dict:
        return await self._get("Alert Service", f"{self._config.url_alert}/internal/alerts/active", {"comunidadId": comunidad_id})


def _valor(v: Any) -> Any:
    """Las fechas viajan en ISO 8601 con zona; los booleanos en minúsculas, como los esperan .NET y Spring."""
    if isinstance(v, datetime):
        return (v if v.tzinfo else v.replace(tzinfo=timezone.utc)).astimezone(timezone.utc).isoformat().replace("+00:00", "Z")
    if isinstance(v, bool):
        return "true" if v else "false"
    return v


class ClienteAuditoria:
    """
    Registra acciones en Audit Service sin bloquear la operación: encola y un ciclo en
    segundo plano envía en lotes. Al apagar se vacía lo pendiente.
    """

    def __init__(self, config: Configuracion, http: httpx.AsyncClient, tokens: ProveedorTokenServicio) -> None:
        self._config = config
        self._http = http
        self._tokens = tokens
        self._cola: asyncio.Queue[dict] = asyncio.Queue(maxsize=10_000)
        self._tarea: asyncio.Task | None = None

    def iniciar(self) -> None:
        self._tarea = asyncio.create_task(self._ciclo())

    async def detener(self) -> None:
        if self._tarea:
            self._tarea.cancel()
        await self._enviar()

    def registrar(self, accion: str, entidad: str, entidad_id: str | None, detalle: dict | None,
                  usuario_id: int | None, usuario_email: str, ip: str | None, correlacion: str | None) -> None:
        evento = {
            "servicio": self._config.servicio_nombre,
            "usuarioId": usuario_id,
            "usuarioEmail": usuario_email or "sistema",
            "accion": accion,
            "entidad": entidad,
            "entidadId": entidad_id,
            "detalle": detalle,
            "direccionIp": ip,
            "correlacion": correlacion,
            "fechaHora": datetime.now(timezone.utc).isoformat(),
        }
        try:
            self._cola.put_nowait(evento)
        except asyncio.QueueFull:
            logger.warning("Cola de auditoría llena: se descarta '%s'.", accion)

    async def _ciclo(self) -> None:
        while True:
            await asyncio.sleep(1)
            await self._enviar()

    async def _enviar(self) -> None:
        lote = []
        while not self._cola.empty() and len(lote) < 200:
            lote.append(self._cola.get_nowait())
        if not lote:
            return
        try:
            respuesta = await self._http.post(
                f"{self._config.url_auditoria}/internal/events/batch",
                json={"eventos": lote},
                headers={"authorization": f"Bearer {await self._tokens.obtener()}"},
            )
            respuesta.raise_for_status()
        except httpx.HTTPError as error:
            logger.warning("No se pudieron registrar %d eventos en auditoría: %s", len(lote), error)

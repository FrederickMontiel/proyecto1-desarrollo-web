"""
Indicadores del dashboard estadístico (sección 15): sensores, comunidades, alertas,
eventos, distribución por nivel y por fenómeno, y estadísticas climáticas, en una sola
respuesta. Se consultan los servicios en paralelo; si uno no responde, su bloque sale como
no disponible y los demás se muestran igual.
"""

import asyncio
from collections import Counter
from datetime import datetime, timedelta, timezone
from typing import Any, Awaitable

from .clientes import ClienteServicios, ServicioNoDisponible


async def _seguro(tarea: Awaitable[Any]) -> Any:
    try:
        return await tarea
    except ServicioNoDisponible:
        return None


async def indicadores(c: ClienteServicios, comunidad_id: int | None, desde: datetime | None, hasta: datetime | None) -> dict:
    fin = hasta or datetime.now(timezone.utc)
    inicio = desde or fin - timedelta(days=30)
    rango = dict(comunidadId=comunidad_id, desde=inicio, hasta=fin)

    comunidades, sensores, alertas, eventos, clima = await asyncio.gather(
        _seguro(c.comunidades()),
        _seguro(c.sensores(comunidadId=comunidad_id)),
        _seguro(c.resumen_alertas(**rango)),
        _seguro(c.estadisticas_eventos(**rango)),
        _seguro(c.estadisticas_lecturas(**rango)),
    )

    if comunidades is not None and comunidad_id:
        comunidades = [x for x in comunidades if x["id"] == comunidad_id]

    estados = Counter(s["estado"] for s in sensores or [])
    return {
        "periodo": {"desde": inicio, "hasta": fin},
        "comunidades": None if comunidades is None else {
            "total": len(comunidades),
            "activas": sum(1 for x in comunidades if x["activa"]),
        },
        "sensores": None if sensores is None else {
            "total": len(sensores),
            "activos": estados.get("Activo", 0),
            "inactivos": estados.get("Inactivo", 0),
            "sinComunicacion": estados.get("SinSenal", 0),
            "porTipo": dict(Counter(s["tipo"] for s in sensores)),
            "porNivel": dict(Counter(s["nivelActual"] for s in sensores if s["estado"] == "Activo" and s.get("nivelActual"))),
        },
        "alertas": alertas,
        "eventos": eventos,
        "estadisticasClimaticas": clima,
        "disponibles": {
            "community": comunidades is not None,
            "sensor": sensores is not None and clima is not None,
            "alert": alertas is not None and eventos is not None,
        },
    }

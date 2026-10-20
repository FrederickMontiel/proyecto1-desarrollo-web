"""HTML (Jinja2) → PDF (WeasyPrint). El PDF se genera siempre en el backend (RNF-REP-01)."""

import asyncio
from datetime import datetime
from pathlib import Path

from jinja2 import Environment, FileSystemLoader, select_autoescape

from .etiquetas import Formato, etiqueta
from .reportes import Documento, Filtros, TipoReporte

PLANTILLAS = Path(__file__).parent / "plantillas"

NOMBRES_FILTRO = {
    "comunidadId": "Comunidad",
    "sensorId": "Sensor",
    "tipoSensor": "Tipo de sensor",
    "estadoSensor": "Estado del sensor",
    "fenomeno": "Fenómeno",
    "nivel": "Nivel",
    "estado": "Estado",
    "activa": "Activa",
    "departamento": "Departamento",
    "desde": "Desde",
    "hasta": "Hasta",
}


class Renderizador:
    def __init__(self, formato: Formato) -> None:
        self._formato = formato
        # autoescape: los mensajes de alerta y nombres vienen de otros servicios y de
        # usuarios; nunca se insertan como HTML.
        self._entorno = Environment(loader=FileSystemLoader(PLANTILLAS), autoescape=select_autoescape(["html"]))

    def html(self, tipo: TipoReporte, documento: Documento, meta: dict, filtros: Filtros,
             nombres: dict[str, str] | None = None) -> str:
        return self._entorno.get_template("reporte.html").render(
            tipo=tipo,
            documento=documento,
            meta=meta,
            generado=self._formato.fecha_hora(meta["generatedAt"]),
            filtros=self.describir_filtros(filtros, nombres or {}),
        )

    def describir_filtros(self, filtros: Filtros, nombres: dict[str, str]) -> list[tuple[str, str]]:
        """Filtros legibles: "Comunidad: Aldea El Rosario" en lugar de "comunidadId: 1"."""
        descritos = []
        for clave, valor in filtros.usados().items():
            if isinstance(valor, datetime):
                texto = self._formato.fecha_hora(valor)
            elif isinstance(valor, bool):
                texto = "Sí" if valor else "No"
            elif clave in nombres:
                texto = nombres[clave]
            else:
                texto = etiqueta(valor)
            descritos.append((NOMBRES_FILTRO.get(clave, clave), texto))
        return descritos

    async def pdf(self, html: str) -> bytes:
        # WeasyPrint usa CPU de forma intensa y es síncrono: se ejecuta en un hilo para no
        # bloquear el bucle de eventos (las demás peticiones siguen atendiéndose).
        # Se importa aquí para que el resto del servicio cargue aunque falten las librerías
        # nativas (Pango) en una máquina de desarrollo.
        from weasyprint import HTML

        return await asyncio.to_thread(lambda: HTML(string=html, base_url=str(PLANTILLAS)).write_pdf())

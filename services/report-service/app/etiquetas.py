"""
Presentación: los enumerados viajan entre servicios con su nombre técnico (``NivelRio``,
``IncendioForestal``, ``SinSenal``); en el PDF se muestran como los lee una persona.
"""

from datetime import datetime, timezone
from decimal import Decimal, InvalidOperation
from zoneinfo import ZoneInfo

ETIQUETAS = {
    # Tipos de sensor
    "Temperatura": "Temperatura",
    "Humedad": "Humedad",
    "Viento": "Viento",
    "Lluvia": "Lluvia",
    "NivelRio": "Nivel de río",
    "NivelReservorio": "Nivel de reservorio",
    "Humo": "Humo",
    "Otro": "Otro",
    # Estados de sensor
    "Activo": "Activo",
    "Inactivo": "Inactivo",
    "SinSenal": "Sin comunicación",
    # Fenómenos
    "Ninguno": "Ninguno",
    "Inundacion": "Inundación",
    "Sequia": "Sequía",
    "Tormenta": "Tormenta",
    "Helada": "Helada",
    "IncendioForestal": "Incendio forestal",
    # Estados de alerta / evento
    "Activa": "Activa",
    "Atendida": "Atendida",
    "Cerrada": "Cerrada",
    # Niveles
    "Verde": "Verde",
    "Amarillo": "Amarillo",
    "Naranja": "Naranja",
    "Rojo": "Rojo",
}

ORDEN_NIVELES = ["Rojo", "Naranja", "Amarillo", "Verde"]


def etiqueta(valor: object) -> str:
    if valor is None:
        return "—"
    return ETIQUETAS.get(str(valor), str(valor))


class Formato:
    """Formatos de fecha y número en la zona horaria configurada (Guatemala por defecto)."""

    def __init__(self, zona: str) -> None:
        self.zona = ZoneInfo(zona)

    def _local(self, valor: str | datetime | None) -> datetime | None:
        if valor is None or valor == "":
            return None
        if isinstance(valor, str):
            valor = datetime.fromisoformat(valor.replace("Z", "+00:00"))
        if valor.tzinfo is None:
            valor = valor.replace(tzinfo=timezone.utc)
        return valor.astimezone(self.zona)

    def fecha(self, valor) -> str:
        local = self._local(valor)
        return local.strftime("%d/%m/%Y") if local else "—"

    def hora(self, valor) -> str:
        local = self._local(valor)
        return local.strftime("%H:%M:%S") if local else "—"

    def fecha_hora(self, valor) -> str:
        local = self._local(valor)
        return local.strftime("%d/%m/%Y %H:%M") if local else "—"

    @staticmethod
    def numero(valor, decimales: int = 2) -> str:
        if valor is None or valor == "":
            return "—"
        try:
            d = Decimal(str(valor))
        except InvalidOperation:
            return str(valor)
        texto = f"{d:,.{decimales}f}"
        # Formato guatemalteco: coma para miles, punto para decimales (como en el resto del sistema).
        return texto.rstrip("0").rstrip(".") if "." in texto else texto

    @staticmethod
    def entero(valor) -> str:
        return "—" if valor is None else f"{int(valor):,}"

    def coordenadas(self, lat, lon) -> str:
        if lat is None or lon is None:
            return "—"
        return f"{float(lat):.5f}, {float(lon):.5f}"

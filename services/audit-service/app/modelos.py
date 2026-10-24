"""Contratos de entrada y salida. Los nombres de campo coinciden con la bitácora de la Fase 2."""

from datetime import datetime, timezone
from typing import Any

from pydantic import BaseModel, ConfigDict, Field, field_validator


class EventoEntrada(BaseModel):
    """Lo que un microservicio envía a ``POST /internal/events``."""

    model_config = ConfigDict(extra="forbid")

    servicio: str = Field(min_length=1, max_length=60)
    usuarioId: int | None = None
    usuarioEmail: str = Field(default="sistema", max_length=160)
    accion: str = Field(min_length=1, max_length=80)
    entidad: str = Field(min_length=1, max_length=80)
    entidadId: str | None = Field(default=None, max_length=80)
    detalle: dict[str, Any] | None = None
    direccionIp: str | None = Field(default=None, max_length=64)
    correlacion: str | None = Field(default=None, max_length=64)
    # Momento en que ocurrió la acción en el servicio de origen. Si no se envía, se usa el
    # de llegada. Se conserva aparte la hora de recepción para detectar relojes desfasados.
    fechaHora: datetime | None = None

    @field_validator("fechaHora")
    @classmethod
    def _a_utc(cls, valor: datetime | None) -> datetime | None:
        if valor is None:
            return None
        return valor.replace(tzinfo=timezone.utc) if valor.tzinfo is None else valor.astimezone(timezone.utc)


class LoteEventos(BaseModel):
    """Inserción masiva: la usa la herramienta de migración para traer la bitácora anterior."""

    model_config = ConfigDict(extra="forbid")

    eventos: list[EventoEntrada] = Field(min_length=1, max_length=5000)


class EventoSalida(BaseModel):
    id: str
    servicio: str
    usuarioId: int | None
    usuarioEmail: str
    accion: str
    entidad: str
    entidadId: str | None
    detalle: dict[str, Any] | None
    direccionIp: str | None
    correlacion: str | None
    fechaHora: datetime


class Pagina(BaseModel):
    elementos: list[EventoSalida]
    pagina: int
    tamanoPagina: int
    totalElementos: int
    totalPaginas: int


class Catalogos(BaseModel):
    acciones: list[str]
    entidades: list[str]
    servicios: list[str]


class Filtro(BaseModel):
    usuario: str | None = None
    accion: str | None = None
    entidad: str | None = None
    servicio: str | None = None
    desde: datetime | None = None
    hasta: datetime | None = None
    pagina: int = Field(default=1, ge=1)
    tamano: int = Field(default=30, ge=1)

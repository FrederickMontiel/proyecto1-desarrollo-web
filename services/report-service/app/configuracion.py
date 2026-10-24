"""Configuración leída de variables de entorno, como en Compose y en un ConfigMap de Kubernetes."""

from functools import lru_cache
from pathlib import Path
from urllib.parse import quote_plus

from pydantic import model_validator
from pydantic_settings import BaseSettings


class Configuracion(BaseSettings):
    mongo_host: str = "mongo"
    mongo_puerto: int = 27017
    mongo_base: str = "report_db"
    mongo_usuario: str = "report_svc"
    mongo_password: str | None = None
    # Si existe *_FILE el secreto sale del archivo: así montan Docker y Kubernetes los Secrets.
    mongo_password_file: Path | None = None

    auth_url_metadatos: str = "http://auth-service:3000/.well-known/openid-configuration"
    jwt_emisor: str = "swmatrc-auth"
    jwt_audiencia: str = "swmatrc-web"
    jwt_audiencia_interna: str = "swmatrc-internal"

    # Credencial con la que este servicio pide su token interno a Auth.
    servicio_nombre: str = "report-service"
    servicio_secreto: str | None = None
    servicio_secreto_file: Path | None = None

    url_auth: str = "http://auth-service:3000"
    url_community: str = "http://community-service:8080"
    url_sensor: str = "http://sensor-service:8080"
    url_alert: str = "http://alert-service:8080"
    url_auditoria: str = "http://audit-service:8000"

    # Las fechas de los reportes se muestran en la hora de Guatemala; se guardan en UTC.
    zona_horaria: str = "America/Guatemala"

    # Un PDF con cientos de miles de filas no sirve para leerse: se trae hasta este número
    # y el reporte avisa cuántas quedaron fuera para que el usuario acote los filtros.
    max_filas_reporte: int = 10_000
    tamano_lote: int = 5000

    # Reportes que se generan a la vez en esta réplica: WeasyPrint es intensivo en CPU.
    generaciones_simultaneas: int = 2

    # Un reporte que sigue "en proceso" después de esto se da por interrumpido (la réplica
    # que lo generaba se reinició).
    minutos_reporte_interrumpido: int = 15

    @model_validator(mode="after")
    def _leer_secretos(self) -> "Configuracion":
        if self.mongo_password_file:
            self.mongo_password = self.mongo_password_file.read_text(encoding="utf-8").strip()
        if self.servicio_secreto_file:
            self.servicio_secreto = self.servicio_secreto_file.read_text(encoding="utf-8").strip()
        if not self.mongo_password:
            raise ValueError("Falta la variable de entorno MONGO_PASSWORD (o MONGO_PASSWORD_FILE).")
        return self

    @property
    def mongo_uri(self) -> str:
        return (
            f"mongodb://{quote_plus(self.mongo_usuario)}:{quote_plus(self.mongo_password or '')}"
            f"@{self.mongo_host}:{self.mongo_puerto}/{self.mongo_base}?authSource={self.mongo_base}"
        )


@lru_cache
def obtener_configuracion() -> Configuracion:
    return Configuracion()

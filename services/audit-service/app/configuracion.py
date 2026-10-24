"""Configuración leída de variables de entorno, como en Compose y en un ConfigMap de Kubernetes."""

from functools import lru_cache
from pathlib import Path
from urllib.parse import quote_plus

from pydantic import model_validator
from pydantic_settings import BaseSettings


class Configuracion(BaseSettings):
    mongo_host: str = "mongo"
    mongo_puerto: int = 27017
    mongo_base: str = "audit_db"
    mongo_usuario: str = "audit_svc"
    mongo_password: str | None = None
    # Igual que en los demás servicios: si existe la variable *_FILE, el secreto sale del
    # archivo, que es como Docker y Kubernetes montan los Secrets.
    mongo_password_file: Path | None = None

    # Documento de descubrimiento del Auth Service (formato OpenID Connect).
    auth_url_metadatos: str = "http://auth-service:3000/.well-known/openid-configuration"
    jwt_emisor: str = "swmatrc-auth"
    jwt_audiencia: str = "swmatrc-web"
    jwt_audiencia_interna: str = "swmatrc-internal"

    tamano_pagina_maximo: int = 200

    @model_validator(mode="after")
    def _leer_secretos(self) -> "Configuracion":
        if self.mongo_password_file:
            self.mongo_password = self.mongo_password_file.read_text(encoding="utf-8").strip()
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

"""
Validación de los JWT emitidos por el Auth Service.

La llave pública se descubre a partir del documento ``openid-configuration`` y se guarda en
memoria por ``kid``. Si llega un token con un ``kid`` desconocido (Auth rotó la llave), se
vuelve a descargar el JWKS, como mucho una vez cada 30 segundos para que un atacante no
pueda forzar descargas mandando tokens inventados.
"""

import asyncio
import json
import time
from dataclasses import dataclass, field
from typing import Annotated, Any

import httpx
import jwt
from fastapi import Depends, HTTPException, Request, status

from .configuracion import Configuracion, obtener_configuracion

PERMISO_INTERNO = "interno"


@dataclass(frozen=True)
class Identidad:
    sujeto: str
    email: str
    nombre: str
    rol: str
    permisos: list[str] = field(default_factory=list)

    @property
    def id_usuario(self) -> int | None:
        return int(self.sujeto) if self.sujeto.isdigit() else None


class ValidadorJwt:
    ESPERA_MINIMA_RECARGA = 30.0

    def __init__(self, configuracion: Configuracion, cliente: httpx.AsyncClient | None = None) -> None:
        self._config = configuracion
        self._cliente = cliente or httpx.AsyncClient(timeout=5.0)
        self._llaves: dict[str, Any] = {}
        self._ultima_recarga = 0.0
        self._candado = asyncio.Lock()

    async def cerrar(self) -> None:
        await self._cliente.aclose()

    async def validar(self, token: str, audiencia: str) -> dict[str, Any]:
        try:
            kid = jwt.get_unverified_header(token).get("kid")
        except jwt.PyJWTError as error:
            raise jwt.InvalidTokenError("Token mal formado.") from error

        llave = await self._llave(kid)
        return jwt.decode(
            token,
            llave,
            algorithms=["RS256"],
            issuer=self._config.jwt_emisor,
            audience=audiencia,
            leeway=60,
            options={"require": ["exp", "iss", "aud", "sub"]},
        )

    async def _llave(self, kid: str | None) -> Any:
        if kid in self._llaves:
            return self._llaves[kid]

        async with self._candado:
            if kid not in self._llaves and time.monotonic() - self._ultima_recarga >= self.ESPERA_MINIMA_RECARGA:
                await self._recargar()

        if kid not in self._llaves:
            raise jwt.InvalidTokenError("El token está firmado con una llave desconocida.")
        return self._llaves[kid]

    async def _recargar(self) -> None:
        self._ultima_recarga = time.monotonic()
        metadatos = (await self._cliente.get(self._config.auth_url_metadatos)).raise_for_status().json()
        jwks = (await self._cliente.get(metadatos["jwks_uri"])).raise_for_status().json()
        self._llaves = {
            clave["kid"]: jwt.algorithms.RSAAlgorithm.from_jwk(json.dumps(clave))
            for clave in jwks.get("keys", [])
            if clave.get("kty") == "RSA" and clave.get("kid")
        }


def _validador(request: Request) -> ValidadorJwt:
    return request.app.state.validador


def _token(request: Request) -> str:
    cabecera = request.headers.get("authorization", "")
    tipo, _, token = cabecera.partition(" ")
    if tipo.lower() != "bearer" or not token:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Falta el token de acceso.")
    return token.strip()


async def _identidad(
    request: Request,
    audiencia: str,
    validador: ValidadorJwt,
) -> Identidad:
    try:
        claims = await validador.validar(_token(request), audiencia)
    except (jwt.PyJWTError, httpx.HTTPError):
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "El token no es válido o ya venció.") from None

    return Identidad(
        sujeto=str(claims["sub"]),
        email=claims.get("email", ""),
        nombre=claims.get("name", ""),
        rol=claims.get("role", ""),
        permisos=list(claims.get("permissions", [])),
    )


def requiere_permiso(permiso: str):
    """Dependencia: token de usuario (audiencia web) que incluya el permiso indicado."""

    async def dependencia(
        request: Request,
        validador: Annotated[ValidadorJwt, Depends(_validador)],
        config: Annotated[Configuracion, Depends(obtener_configuracion)],
    ) -> Identidad:
        identidad = await _identidad(request, config.jwt_audiencia, validador)
        if permiso not in identidad.permisos:
            raise HTTPException(status.HTTP_403_FORBIDDEN, "No tiene permisos para realizar esta operación.")
        return identidad

    return dependencia


async def solo_servicios(
    request: Request,
    validador: Annotated[ValidadorJwt, Depends(_validador)],
    config: Annotated[Configuracion, Depends(obtener_configuracion)],
) -> Identidad:
    """Dependencia: token de servicio (audiencia interna). Un token de usuario no sirve aquí."""
    identidad = await _identidad(request, config.jwt_audiencia_interna, validador)
    if PERMISO_INTERNO not in identidad.permisos:
        raise HTTPException(status.HTTP_403_FORBIDDEN, "La ruta es exclusiva de los servicios internos.")
    return identidad

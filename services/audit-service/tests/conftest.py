import json
import time
import uuid
from contextlib import asynccontextmanager

import httpx
import jwt
import pytest
from cryptography.hazmat.primitives.asymmetric import rsa
from fastapi.testclient import TestClient

from app.configuracion import Configuracion, obtener_configuracion
from app.main import crear_app
from app.modelos import Catalogos, EventoEntrada, EventoSalida, Filtro, Pagina
from app.seguridad import ValidadorJwt

KID = "llave-de-prueba"
URL_METADATOS = "http://auth.prueba/.well-known/openid-configuration"


class RepositorioMemoria:
    """Implementa el mismo protocolo que RepositorioMongo, sin base de datos."""

    def __init__(self) -> None:
        self.eventos: list[EventoSalida] = []

    async def insertar(self, eventos: list[EventoEntrada]) -> int:
        from datetime import datetime, timezone

        for evento in eventos:
            datos = evento.model_dump()
            datos["fechaHora"] = evento.fechaHora or datetime.now(timezone.utc)
            self.eventos.append(EventoSalida(id=uuid.uuid4().hex, **datos))
        return len(eventos)

    async def buscar(self, filtro: Filtro) -> Pagina:
        filas = [
            e
            for e in self.eventos
            if (not filtro.accion or e.accion == filtro.accion)
            and (not filtro.usuario or filtro.usuario.lower() in e.usuarioEmail.lower())
        ]
        filas.sort(key=lambda e: e.fechaHora, reverse=True)
        inicio = (filtro.pagina - 1) * filtro.tamano
        return Pagina(
            elementos=filas[inicio : inicio + filtro.tamano],
            pagina=filtro.pagina,
            tamanoPagina=filtro.tamano,
            totalElementos=len(filas),
            totalPaginas=-(-len(filas) // filtro.tamano),
        )

    async def catalogos(self) -> Catalogos:
        return Catalogos(
            acciones=sorted({e.accion for e in self.eventos}),
            entidades=sorted({e.entidad for e in self.eventos}),
            servicios=sorted({e.servicio for e in self.eventos}),
        )


@pytest.fixture(scope="session")
def llave_privada():
    return rsa.generate_private_key(public_exponent=65537, key_size=2048)


@pytest.fixture
def config() -> Configuracion:
    return Configuracion(mongo_password="no-se-usa", auth_url_metadatos=URL_METADATOS)


@pytest.fixture
def auth_simulado(llave_privada):
    """Imita los dos documentos que publica el Auth Service real."""
    jwk = json.loads(jwt.algorithms.RSAAlgorithm.to_jwk(llave_privada.public_key()))
    jwk.update(kid=KID, alg="RS256", use="sig")
    llamadas = {"jwks": 0}

    def responder(peticion: httpx.Request) -> httpx.Response:
        if peticion.url.path.endswith("openid-configuration"):
            return httpx.Response(200, json={"jwks_uri": "http://auth.prueba/.well-known/jwks.json"})
        llamadas["jwks"] += 1
        return httpx.Response(200, json={"keys": [jwk]})

    return httpx.MockTransport(responder), llamadas


@pytest.fixture
def repositorio() -> RepositorioMemoria:
    return RepositorioMemoria()


@pytest.fixture
def cliente(config, auth_simulado, repositorio):
    transporte, _ = auth_simulado

    @asynccontextmanager
    async def ciclo(app):
        app.state.repositorio = repositorio
        app.state.validador = ValidadorJwt(config, httpx.AsyncClient(transport=transporte))
        yield
        await app.state.validador.cerrar()

    app = crear_app(ciclo)
    app.dependency_overrides[obtener_configuracion] = lambda: config
    with TestClient(app) as c:
        yield c


@pytest.fixture
def emitir(llave_privada):
    """Firma tokens como lo haría el Auth Service, con la posibilidad de alterar cualquier claim."""

    def _emitir(*, audiencia="swmatrc-web", permisos=(), rol="Administrador", sub="1", llave=None, **extra):
        ahora = int(time.time())
        claims = {
            "sub": sub,
            "email": "admin@swmatrc.local",
            "name": "Admin",
            "role": rol,
            "permissions": list(permisos),
            "iss": "swmatrc-auth",
            "aud": audiencia,
            "iat": ahora,
            "exp": ahora + 600,
            "jti": str(uuid.uuid4()),
            **extra,
        }
        return jwt.encode(claims, llave or llave_privada, algorithm="RS256", headers={"kid": KID})

    return _emitir

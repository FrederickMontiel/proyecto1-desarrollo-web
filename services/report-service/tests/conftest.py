import json
import time
import uuid
from contextlib import asynccontextmanager
from datetime import datetime, timezone

import httpx
import jwt
import pytest
from cryptography.hazmat.primitives.asymmetric import rsa
from fastapi.testclient import TestClient

from app.clientes import ServicioNoDisponible
from app.configuracion import Configuracion, obtener_configuracion
from app.main import crear_app
from app.seguridad import ValidadorJwt
from app.servicio import ServicioReportes

KID = "llave-de-prueba"
URL_METADATOS = "http://auth.prueba/.well-known/openid-configuration"


# --- Datos que normalmente vienen de Community, Sensor y Alert -------------------------------

COMUNIDADES = [
    {"id": 1, "nombre": "Aldea El Rosario", "municipio": "Escuintla", "departamento": "Escuintla", "pais": "Guatemala",
     "latitud": 14.3, "longitud": -90.78, "poblacion": 850, "activa": True},
    {"id": 2, "nombre": "Caserío Las Flores", "municipio": "Flores", "departamento": "Petén", "pais": "Guatemala",
     "latitud": 16.93, "longitud": -89.89, "poblacion": 420, "activa": False},
]

SENSORES = [
    {"id": 1, "comunidadId": 1, "comunidadNombre": "Aldea El Rosario", "codigo": "TEMP-01", "nombre": "Termómetro plaza",
     "tipo": "Temperatura", "unidadMedida": "°C", "estado": "Activo", "ubicacion": "Plaza central",
     "fechaInstalacion": "2026-01-10T00:00:00Z", "valorActual": 24.5, "ultimaLectura": "2026-10-22T18:00:00Z", "nivelActual": "Verde"},
    {"id": 3, "comunidadId": 1, "comunidadNombre": "Aldea El Rosario", "codigo": "RIO-01", "nombre": "Limnímetro puente",
     "tipo": "NivelRio", "unidadMedida": "m", "estado": "SinSenal", "ubicacion": None,
     "fechaInstalacion": None, "valorActual": 3.9, "ultimaLectura": "2026-10-22T17:40:00Z", "nivelActual": "Naranja"},
]


def lectura(i):
    return {"id": i, "sensorId": 1, "sensorCodigo": "TEMP-01", "sensorNombre": "Termómetro plaza", "comunidadId": 1,
            "comunidadNombre": "Aldea El Rosario", "tipo": "Temperatura", "valor": 20 + i / 10, "unidadMedida": "°C",
            "estadoSensor": "Activo", "nivel": "Verde", "fechaHora": f"2026-10-22T{10 + i % 10:02d}:00:00Z"}


ALERTA = {"id": 7, "comunidadId": 1, "comunidadNombre": "Aldea El Rosario", "sensorId": 3, "sensorCodigo": "RIO-01",
          "nivel": "Rojo", "fenomeno": "Inundacion", "mensaje": "EMERGENCIA por inundación <b>¡ya!</b>", "valorDisparo": 4.8,
          "umbral": 4.5, "estado": "Activa", "fechaHora": "2026-10-22T18:05:00Z"}


class DatosFalsos:
    """Implementa la misma interfaz que ClienteServicios con datos fijos."""

    def __init__(self) -> None:
        self.caidos: set[str] = set()
        self.total_lecturas = 3
        self.llamadas: list[tuple[str, dict]] = []

    def _revisar(self, servicio):
        if servicio in self.caidos:
            raise ServicioNoDisponible(f"{servicio} no respondió; intente de nuevo en unos minutos.")

    async def comunidades(self, activa=None):
        self._revisar("Community Service")
        return [c for c in COMUNIDADES if activa is None or c["activa"] == activa]

    async def conteo_sensores(self):
        self._revisar("Sensor Service")
        return {1: {"comunidadId": 1, "total": 2, "activos": 1}}

    async def sensores(self, **f):
        self._revisar("Sensor Service")
        return [s for s in SENSORES if not f.get("comunidadId") or s["comunidadId"] == f["comunidadId"]]

    async def lecturas(self, limite, **f):
        self._revisar("Sensor Service")
        self.llamadas.append(("lecturas", f))
        filas = [lectura(i) for i in range(self.total_lecturas)]
        return filas[:limite], self.total_lecturas

    async def estadisticas_lecturas(self, **f):
        self._revisar("Sensor Service")
        return [{"tipo": "Temperatura", "unidadMedida": "°C", "lecturas": 100, "promedio": 23.4, "minimo": 18, "maximo": 31, "suma": 2340},
                {"tipo": "Lluvia", "unidadMedida": "mm", "lecturas": 100, "promedio": 2.5, "minimo": 0, "maximo": 30.7, "suma": 250}]

    async def alertas(self, limite, **f):
        self._revisar("Alert Service")
        return [ALERTA], 1

    async def eventos(self, limite, **f):
        self._revisar("Alert Service")
        return [{"id": 1, "comunidadNombre": "Aldea El Rosario", "fenomeno": "Helada", "nivelMaximo": "Naranja",
                 "origenSensor": "TEMP-01 — Termómetro plaza", "valorRegistrado": 1.6, "estado": "Cerrada",
                 "usuarioResponsable": None, "fechaInicio": "2026-10-22T06:00:00Z", "fechaFin": "2026-10-22T06:20:00Z",
                 "duracionMinutos": 20.0}], 1

    async def estadisticas_eventos(self, **f):
        self._revisar("Alert Service")
        return {"totalEventos": 1, "eventosAbiertos": 0, "duracionPromedioMinutos": 20.0,
                "porFenomeno": {"Helada": 1}, "porNivel": {"Naranja": 1}, "porComunidad": {"Aldea El Rosario": 1}, "porDia": {}}

    async def resumen_alertas(self, **f):
        self._revisar("Alert Service")
        return {"alertasActivas": 1, "nivelGlobal": "Rojo", "alertasPorNivel": {"Rojo": 1}, "alertasPorFenomeno": {"Inundacion": 1},
                "eventosUltimas24h": 1, "eventosPorFenomeno": {"Helada": 1}, "alertasEnPeriodo": 3,
                "alertasPeriodoPorNivel": {"Rojo": 1, "Naranja": 2}, "alertasPeriodoPorFenomeno": {"Inundacion": 3}}

    async def alertas_activas(self, comunidad_id=None):
        self._revisar("Alert Service")
        return {"nivelGlobal": "Rojo", "alertas": [ALERTA]}


class RepositorioMemoria:
    def __init__(self) -> None:
        self.reportes: dict[str, dict] = {}
        self.archivos: dict[str, bytes] = {}
        self.contador = 0

    async def crear_indices(self):
        pass

    async def siguiente_id(self, ahora):
        self.contador += 1
        return f"REP-{ahora.year}-{self.contador:06d}"

    async def insertar(self, d):
        self.reportes[d["reportId"]] = dict(d)

    async def actualizar(self, rid, cambios, condicion=None):
        self.reportes[rid].update(cambios)
        return True

    async def obtener(self, rid):
        d = self.reportes.get(rid)
        return dict(d) if d else None

    async def listar(self, filtro, pagina, tamano):
        filas = [d for d in self.reportes.values()
                 if all((d["generatedBy"]["id"] if k == "generatedBy.id" else d.get(k)) == v for k, v in filtro.items())]
        filas.sort(key=lambda d: d["generatedAt"], reverse=True)
        return {"elementos": filas[(pagina - 1) * tamano: pagina * tamano], "pagina": pagina, "tamanoPagina": tamano,
                "totalElementos": len(filas), "totalPaginas": -(-len(filas) // tamano)}

    async def registrar_descarga(self, rid, usuario, ahora):
        d = self.reportes[rid]
        d["downloads"].append({"by": usuario, "at": ahora})
        d["lastDownloadAt"] = ahora
        d["downloadCount"] += 1

    async def guardar_pdf(self, nombre, contenido, rid):
        self.archivos[rid] = contenido
        return rid

    async def leer_pdf(self, archivo_id):
        return self.archivos[archivo_id]


class RenderizadorFalso:
    """Genera el HTML de verdad (Jinja2) pero no el PDF: así las pruebas corren sin Pango."""

    def __init__(self, real) -> None:
        self.real = real
        self.ultimo_html = ""

    def html(self, *args, **kwargs):
        self.ultimo_html = self.real.html(*args, **kwargs)
        return self.ultimo_html

    async def pdf(self, html):
        return b"%PDF-1.7 falso"


class AuditoriaMemoria:
    def __init__(self) -> None:
        self.acciones: list[str] = []

    def registrar(self, accion, *args, **kwargs):
        self.acciones.append(accion)


# --- Infraestructura de pruebas ---------------------------------------------------------------


@pytest.fixture(scope="session")
def llave_privada():
    return rsa.generate_private_key(public_exponent=65537, key_size=2048)


@pytest.fixture
def config() -> Configuracion:
    return Configuracion(mongo_password="no-se-usa", auth_url_metadatos=URL_METADATOS)


@pytest.fixture
def datos():
    return DatosFalsos()


@pytest.fixture
def repositorio():
    return RepositorioMemoria()


@pytest.fixture
def auditoria():
    return AuditoriaMemoria()


@pytest.fixture
def servicio(config, repositorio, datos, auditoria):
    from app.etiquetas import Formato
    from app.pdf import Renderizador

    return ServicioReportes(config, repositorio, datos, auditoria, RenderizadorFalso(Renderizador(Formato(config.zona_horaria))))


@pytest.fixture
def cliente(config, llave_privada, servicio, datos):
    jwk = json.loads(jwt.algorithms.RSAAlgorithm.to_jwk(llave_privada.public_key()))
    jwk.update(kid=KID, alg="RS256", use="sig")

    def responder(peticion: httpx.Request) -> httpx.Response:
        if peticion.url.path.endswith("openid-configuration"):
            return httpx.Response(200, json={"jwks_uri": "http://auth.prueba/.well-known/jwks.json"})
        return httpx.Response(200, json={"keys": [jwk]})

    @asynccontextmanager
    async def ciclo(app):
        app.state.servicio = servicio
        app.state.datos = datos
        app.state.validador = ValidadorJwt(config, httpx.AsyncClient(transport=httpx.MockTransport(responder)))
        yield
        await servicio.esperar_tareas(5)
        await app.state.validador.cerrar()

    app = crear_app(ciclo)
    app.dependency_overrides[obtener_configuracion] = lambda: config
    with TestClient(app) as c:
        yield c


ADMIN = ["reportes:generar", "reportes:descargar", "comunidades:leer", "sensores:leer", "alertas:leer"]
OPERADOR = ["reportes:generar", "comunidades:leer", "sensores:leer", "alertas:leer"]
CONSULTA = ["comunidades:leer", "sensores:leer", "alertas:leer"]


@pytest.fixture
def token(llave_privada):
    def _token(permisos, sub="1", email="admin@swmatrc.local", nombre="Administrador", **extra):
        ahora = int(time.time())
        claims = {"sub": sub, "email": email, "name": nombre, "role": "x", "permissions": permisos,
                  "iss": "swmatrc-auth", "aud": "swmatrc-web", "iat": ahora, "exp": ahora + 600, "jti": str(uuid.uuid4()), **extra}
        return {"authorization": "Bearer " + jwt.encode(claims, llave_privada, algorithm="RS256", headers={"kid": KID})}

    return _token


def esperar(cliente, report_id, cabecera, estados=("COMPLETED", "FAILED"), segundos=5.0):
    """La generación es asíncrona: se consulta el estado hasta que termina."""
    limite = time.time() + segundos
    while time.time() < limite:
        d = cliente.get(f"/api/reports/{report_id}", headers=cabecera).json()
        if d.get("status") in estados:
            return d
        time.sleep(0.05)
    raise AssertionError(f"El reporte {report_id} no terminó: {d}")


def ahora():
    return datetime.now(timezone.utc)

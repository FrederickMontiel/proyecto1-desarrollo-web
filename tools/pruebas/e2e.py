"""
Pruebas de integración de punta a punta de SWMATRC (Fase 3).

Recorre los seis microservicios a través del API Gateway, como lo hará el frontend:
autenticación, permisos, comunidades, sensores, simulación, evaluación de alertas, tiempo
real (SignalR), reportes PDF, auditoría y dashboard. Crea sus propios datos con un sufijo
único, así que puede ejecutarse las veces que haga falta, y al final los desactiva.

    python tools/pruebas/e2e.py                       # contra http://127.0.0.1:10000
    GATEWAY=https://mi-dominio python tools/pruebas/e2e.py

Para la prueba de SignalR: ``npm install`` dentro de tools/pruebas (si no, se omite).
Solo usa la librería estándar de Python.
"""

import json
import os
import shutil
import subprocess
import sys
import time
import traceback
import urllib.error
import urllib.request
from pathlib import Path

RAIZ = Path(__file__).resolve().parents[2]
AQUI = Path(__file__).resolve().parent
# En Windows "localhost" intenta primero IPv6 (::1) y Docker publica el puerto solo en IPv4:
# cada petición esperaría ~2 s a que ese intento falle. Se usa la dirección IPv4 directamente.
GATEWAY = os.environ.get("GATEWAY", "http://127.0.0.1:10000").rstrip("/")
SUFIJO = time.strftime("%H%M%S")

sys.stdout.reconfigure(encoding="utf-8")


def leer_env() -> dict[str, str]:
    valores = {}
    archivo = RAIZ / ".env"
    if archivo.exists():
        for linea in archivo.read_text(encoding="utf-8").splitlines():
            if "=" in linea and not linea.lstrip().startswith("#"):
                clave, _, valor = linea.partition("=")
                valores[clave.strip()] = valor.strip()
    return valores


ENV = leer_env()
ADMIN_EMAIL = os.environ.get("ADMIN_EMAIL", ENV.get("ADMIN_EMAIL", "admin@swmatrc.local"))
ADMIN_PASSWORD = os.environ.get("ADMIN_PASSWORD", ENV.get("ADMIN_PASSWORD", ""))


# =============================================================================================
# HTTP
# =============================================================================================


class Respuesta:
    def __init__(self, estado: int, cabeceras: dict, cuerpo: bytes) -> None:
        self.estado = estado
        self.cabeceras = {k.lower(): v for k, v in cabeceras.items()}
        self.cuerpo = cuerpo

    @property
    def json(self):
        return json.loads(self.cuerpo.decode("utf-8")) if self.cuerpo else None

    @property
    def detalle(self) -> str:
        try:
            return self.json.get("detail", "")
        except Exception:
            return ""

    def __repr__(self) -> str:
        return f"HTTP {self.estado} {self.cuerpo[:200]!r}"


def http(metodo: str, ruta: str, token: str | None = None, cuerpo=None, cabeceras: dict | None = None) -> Respuesta:
    datos = None if cuerpo is None else json.dumps(cuerpo).encode("utf-8")
    peticion = urllib.request.Request(GATEWAY + ruta, data=datos, method=metodo)
    peticion.add_header("Accept", "application/json")
    if datos is not None:
        peticion.add_header("Content-Type", "application/json; charset=utf-8")
    if token:
        peticion.add_header("Authorization", f"Bearer {token}")
    for clave, valor in (cabeceras or {}).items():
        peticion.add_header(clave, valor)
    try:
        with urllib.request.urlopen(peticion, timeout=30) as r:
            return Respuesta(r.status, dict(r.headers), r.read())
    except urllib.error.HTTPError as e:
        return Respuesta(e.code, dict(e.headers), e.read())


def login(email: str, password: str) -> Respuesta:
    """Respeta el límite de 10 logins por minuto del gateway: si se alcanza, espera y reintenta."""
    for _ in range(3):
        r = http("POST", "/api/auth/login", cuerpo={"email": email, "password": password})
        if r.estado != 429:
            return r
        espera = int(r.cabeceras.get("retry-after", "30"))
        print(f"      (límite de login alcanzado; esperando {espera} s)")
        time.sleep(espera + 1)
    return r


def token_de(email: str, password: str) -> str:
    r = login(email, password)
    assert r.estado == 200, f"login de {email}: {r}"
    return r.json["token"]


def esperar(condicion, segundos: float = 20, intervalo: float = 1.0, mensaje: str = "la condición"):
    limite = time.time() + segundos
    ultimo = None
    while time.time() < limite:
        ultimo = condicion()
        if ultimo:
            return ultimo
        time.sleep(intervalo)
    raise AssertionError(f"No se cumplió {mensaje} en {segundos} s (último valor: {ultimo!r}).")


def esperado(r: Respuesta, estado: int, contiene: str | None = None) -> Respuesta:
    assert r.estado == estado, f"se esperaba {estado} y llegó {r}"
    if contiene:
        assert contiene in r.detalle, f"el detalle no contiene {contiene!r}: {r.detalle!r}"
    return r


def problema(r: Respuesta) -> None:
    assert r.cabeceras.get("content-type", "").startswith("application/problem+json"), r.cabeceras.get("content-type")
    assert {"title", "status", "detail"} <= set(r.json), r.json


# =============================================================================================
# Registro de pruebas
# =============================================================================================

PRUEBAS: list[tuple[str, str, callable]] = []
C: dict = {}  # contexto compartido entre pruebas (tokens, ids creados)


def prueba(seccion: str):
    def registrar(funcion):
        PRUEBAS.append((seccion, funcion.__doc__ or funcion.__name__, funcion))
        return funcion
    return registrar


# =============================================================================================
# Gateway
# =============================================================================================


@prueba("Gateway")
def gw_salud():
    """Sonda /health/live del gateway"""
    assert http("GET", "/health/live").estado == 200


@prueba("Gateway")
def gw_sin_token():
    """Ruta protegida sin token → 401 ProblemDetails"""
    r = esperado(http("GET", "/api/communities"), 401, "Falta el token")
    problema(r)


@prueba("Gateway")
def gw_token_alterado():
    """Token con la firma alterada → 401"""
    problema(esperado(http("GET", "/api/communities", token=C["admin"][:-3] + "abc"), 401, "no es válido"))


@prueba("Gateway")
def gw_internos_ocultos():
    """Las rutas /internal de los servicios no se exponen → 404"""
    for ruta in ("/internal/token", "/internal/events", "/internal/evaluate", "/internal/communities"):
        assert http("POST", ruta).estado == 404, ruta


@prueba("Gateway")
def gw_correlacion():
    """Propaga el X-Correlation-Id del cliente y oculta la cabecera Server"""
    r = http("GET", "/api/auth/me", token=C["admin"], cabeceras={"X-Correlation-Id": f"e2e-{SUFIJO}"})
    assert r.cabeceras.get("x-correlation-id") == f"e2e-{SUFIJO}", r.cabeceras
    assert "server" not in r.cabeceras, r.cabeceras.get("server")


# =============================================================================================
# Auth
# =============================================================================================


@prueba("Auth")
def auth_credenciales_malas():
    """Contraseña incorrecta → 401 'Credenciales inválidas'"""
    esperado(login(ADMIN_EMAIL, "incorrecta-123"), 401, "Credenciales inválidas")


@prueba("Auth")
def auth_token_rs256():
    """El token es RS256 con kid, emisor, audiencia, rol y permisos"""
    import base64

    cabecera, carga, _ = C["admin"].split(".")
    decodificar = lambda s: json.loads(base64.urlsafe_b64decode(s + "=" * (-len(s) % 4)))
    h, p = decodificar(cabecera), decodificar(carga)
    assert h["alg"] == "RS256" and h.get("kid"), h
    assert p["iss"] == "swmatrc-auth" and p["aud"] == "swmatrc-web", p
    assert p["role"] == "Administrador" and "reportes:descargar" in p["permissions"], p


@prueba("Auth")
def auth_me():
    """GET /api/auth/me devuelve el perfil con permisos"""
    yo = esperado(http("GET", "/api/auth/me", token=C["admin"]), 200).json
    assert yo["email"] == ADMIN_EMAIL and yo["rol"] == "Administrador" and len(yo["permisos"]) >= 13, yo


@prueba("Auth")
def auth_roles():
    """Catálogo de roles: Consulta, Operador, Administrador"""
    roles = {r["codigo"]: len(r["permisos"]) for r in esperado(http("GET", "/api/auth/roles", token=C["admin"]), 200).json}
    assert set(roles) == {"Consulta", "Operador", "Administrador"} and roles["Consulta"] < roles["Operador"] < roles["Administrador"], roles


@prueba("Auth")
def auth_crear_usuarios():
    """Crear un Operador y un Consulta; correo duplicado → 409"""
    for rol in ("Operador", "Consulta"):
        email = f"e2e.{rol.lower()}.{SUFIJO}@ejemplo.org"
        u = esperado(http("POST", "/api/auth/users", token=C["admin"], cuerpo={
            "nombreCompleto": f"E2E {rol}", "email": email.upper(), "password": "Prueba.2026", "rol": rol}), 201).json
        assert u["email"] == email, "el correo se guarda en minúsculas"
        C[f"id_{rol}"], C[f"email_{rol}"] = u["id"], email
    esperado(http("POST", "/api/auth/users", token=C["admin"], cuerpo={
        "nombreCompleto": "x", "email": C["email_Operador"], "password": "Prueba.2026", "rol": "Consulta"}), 409)
    C["operador"] = token_de(C["email_Operador"], "Prueba.2026")
    C["consulta"] = token_de(C["email_Consulta"], "Prueba.2026")


@prueba("Auth")
def auth_validaciones():
    """Campo no permitido y datos inválidos → 400 en español"""
    esperado(http("POST", "/api/auth/users", token=C["admin"], cuerpo={
        "nombreCompleto": "", "email": "no-es-correo", "password": "corta", "rol": "Jefe"}), 400, "no es válido")
    esperado(http("POST", "/api/auth/users", token=C["admin"], cuerpo={
        "nombreCompleto": "x", "email": "a@b.org", "password": "Prueba.2026", "esAdmin": True}), 400, "no está permitido")


@prueba("Auth")
def auth_permisos():
    """Un Consulta no administra usuarios → 403"""
    esperado(http("GET", "/api/auth/users", token=C["consulta"]), 403)


@prueba("Auth")
def auth_cambio_rol_revoca_sesion():
    """Cambiar el rol cierra las sesiones abiertas del usuario"""
    viejo = token_de(C["email_Consulta"], "Prueba.2026")
    esperado(http("PUT", f"/api/auth/users/{C['id_Consulta']}", token=C["admin"],
                  cuerpo={"nombreCompleto": "E2E Consulta", "rol": "Operador"}), 200)
    esperado(http("GET", "/api/auth/me", token=viejo), 401)
    esperado(http("PUT", f"/api/auth/users/{C['id_Consulta']}", token=C["admin"],
                  cuerpo={"nombreCompleto": "E2E Consulta", "rol": "Consulta"}), 200)
    C["consulta"] = token_de(C["email_Consulta"], "Prueba.2026")


@prueba("Auth")
def auth_deshabilitar():
    """Una cuenta deshabilitada no inicia sesión y su token deja de servir"""
    token = token_de(C["email_Operador"], "Prueba.2026")
    esperado(http("PATCH", f"/api/auth/users/{C['id_Operador']}/status", token=C["admin"], cuerpo={"activo": False}), 200)
    esperado(http("GET", "/api/auth/me", token=token), 401)
    esperado(login(C["email_Operador"], "Prueba.2026"), 401, "deshabilitada")
    esperado(http("PATCH", f"/api/auth/users/{C['id_Operador']}/status", token=C["admin"], cuerpo={"activo": True}), 200)
    C["operador"] = token_de(C["email_Operador"], "Prueba.2026")


@prueba("Auth")
def auth_password():
    """Cambio de contraseña propia: la actual mal → 400"""
    esperado(http("POST", "/api/auth/me/password", token=C["operador"],
                  cuerpo={"passwordActual": "otra-cosa", "passwordNueva": "Nueva.2026x"}), 400, "no es correcta")


@prueba("Auth")
def auth_ultimo_admin():
    """El administrador no puede deshabilitarse a sí mismo → 400"""
    yo = http("GET", "/api/auth/me", token=C["admin"]).json
    esperado(http("PATCH", f"/api/auth/users/{yo['id']}/status", token=C["admin"], cuerpo={"activo": False}), 400)


@prueba("Auth")
def auth_logout():
    """Logout revoca el token aunque no haya vencido"""
    token = token_de(C["email_Consulta"], "Prueba.2026")
    esperado(http("POST", "/api/auth/logout", token=token), 204)
    esperado(http("GET", "/api/auth/me", token=token), 401, "sesión fue cerrada")


# =============================================================================================
# Community
# =============================================================================================


def comunidad(nombre: str, **extra) -> dict:
    return {"nombre": nombre, "municipio": "Tacaná", "departamento": "San Marcos", "pais": "Guatemala",
            "latitud": 15.24, "longitud": -92.07, "poblacion": 1200, **extra}


@prueba("Community")
def com_crear():
    """Crear dos comunidades (una para alertas) con acentos; nombre duplicado → 409"""
    a = esperado(http("POST", "/api/communities", token=C["admin"], cuerpo=comunidad(f"E2E Aldea Señorío {SUFIJO}")), 201).json
    b = esperado(http("POST", "/api/communities", token=C["admin"], cuerpo=comunidad(f"E2E Caserío Helado {SUFIJO}")), 201).json
    assert a["nombre"] == f"E2E Aldea Señorío {SUFIJO}", a["nombre"]
    C["com_a"], C["com_b"] = a["id"], b["id"]
    esperado(http("POST", "/api/communities", token=C["admin"], cuerpo=comunidad(f"e2e aldea SEÑORÍO {SUFIJO}")), 409, "Ya existe")


@prueba("Community")
def com_validacion():
    """Latitud fuera de rango y nombre vacío → 400; JSON roto → 400"""
    esperado(http("POST", "/api/communities", token=C["admin"], cuerpo=comunidad("", latitud=120)), 400, "latitud")
    esperado(http("POST", "/api/communities", token=C["admin"], cuerpo={"nombre": "x", "latitud": "norte"}), 400, "JSON")


@prueba("Community")
def com_consultar():
    """Obtener, buscar sin distinguir mayúsculas, filtrar por departamento y 404"""
    assert esperado(http("GET", f"/api/communities/{C['com_a']}", token=C["consulta"]), 200).json["id"] == C["com_a"]
    encontradas = esperado(http("GET", f"/api/communities?busqueda=se%C3%B1or%C3%ADo%20{SUFIJO}", token=C["consulta"]), 200).json
    assert [c["id"] for c in encontradas] == [C["com_a"]], encontradas
    por_depto = http("GET", "/api/communities?departamento=san%20marcos", token=C["consulta"]).json
    assert {C["com_a"], C["com_b"]} <= {c["id"] for c in por_depto}
    esperado(http("GET", "/api/communities/999999", token=C["consulta"]), 404)


@prueba("Community")
def com_actualizar():
    """Actualizar población y descripción"""
    c = esperado(http("PUT", f"/api/communities/{C['com_a']}", token=C["admin"],
                      cuerpo=comunidad(f"E2E Aldea Señorío {SUFIJO}", poblacion=1500, descripcion="Junto al río")), 200).json
    assert c["poblacion"] == 1500 and c["descripcion"] == "Junto al río" and c["fechaModificacion"]


@prueba("Community")
def com_geojson():
    """GeoJSON con content-type application/geo+json y coordenadas [lon, lat]"""
    r = esperado(http("GET", "/api/communities/geo", token=C["consulta"]), 200)
    assert r.cabeceras["content-type"].startswith("application/geo+json")
    f = next(f for f in r.json["features"] if f["id"] == C["com_a"])
    assert f["geometry"]["coordinates"] == [-92.07, 15.24], f["geometry"]


@prueba("Community")
def com_permisos():
    """Un Consulta lee pero no crea comunidades → 403"""
    esperado(http("POST", "/api/communities", token=C["consulta"], cuerpo=comunidad("x")), 403)


# =============================================================================================
# Sensor
# =============================================================================================


@prueba("Sensor")
def sen_plantillas():
    """Plantillas físicas por tipo"""
    tipos = {p["tipo"]: p for p in esperado(http("GET", "/api/sensors/templates", token=C["consulta"]), 200).json}
    assert tipos["NivelRio"]["unidad"] == "m" and tipos["Temperatura"]["valorMaximo"] == 50, tipos["NivelRio"]


@prueba("Sensor")
def sen_crear():
    """Crear sensores con la plantilla; código duplicado → 409; comunidad inexistente → 404"""
    def crear(com, codigo, tipo):
        return esperado(http("POST", "/api/sensors", token=C["admin"], cuerpo={
            "comunidadId": com, "codigo": codigo, "nombre": f"E2E {tipo}", "tipo": tipo}), 201).json

    t = crear(C["com_a"], f"e2e-t{SUFIJO}", "Temperatura")
    assert t["codigo"] == f"E2E-T{SUFIJO}" and t["unidadMedida"] == "°C" and t["comunidadNombre"].startswith("E2E Aldea")
    assert (t["latitud"], t["longitud"]) == (15.24, -92.07), "toma la ubicación de la comunidad"
    C["sen_t"] = t["id"]
    C["sen_r"] = crear(C["com_a"], f"e2e-r{SUFIJO}", "NivelRio")["id"]
    C["sen_h"] = crear(C["com_b"], f"e2e-h{SUFIJO}", "Temperatura")["id"]
    esperado(http("POST", "/api/sensors", token=C["admin"], cuerpo={
        "comunidadId": C["com_a"], "codigo": f"E2E-T{SUFIJO}", "nombre": "x", "tipo": "Temperatura"}), 409)
    esperado(http("POST", "/api/sensors", token=C["admin"], cuerpo={
        "comunidadId": 999999, "codigo": f"e2e-x{SUFIJO}", "nombre": "x", "tipo": "Temperatura"}), 404)


@prueba("Sensor")
def sen_conteo_en_community():
    """Community muestra la cantidad real de sensores (consulta a Sensor por API)"""
    def conteo():
        c = http("GET", f"/api/communities/{C['com_a']}", token=C["admin"]).json
        return c if c["conteoSensoresDisponible"] and c["totalSensores"] == 2 else None
    esperar(conteo, 25, mensaje="que Community vea 2 sensores (caché de 15 s)")


@prueba("Sensor")
def sen_simulacion():
    """La simulación genera lecturas con nivel (evaluado por Alert) cada pocos segundos"""
    def lecturas():
        p = http("GET", f"/api/sensors/readings?sensorId={C['sen_t']}", token=C["consulta"]).json
        return p if p["totalElementos"] >= 2 else None
    pagina = esperar(lecturas, 20, mensaje="lecturas simuladas")
    lectura = pagina["elementos"][0]
    assert lectura["nivel"] in ("Verde", "Amarillo", "Naranja", "Rojo"), lectura
    assert lectura["fechaHora"].endswith("Z"), "fecha en UTC"


@prueba("Sensor")
def sen_valor_forzado():
    """Forzar un valor; fuera del rango → 400"""
    l = esperado(http("POST", f"/api/sensors/{C['sen_r']}/value", token=C["operador"], cuerpo={"valor": 2.5}), 200).json
    assert l["valor"] == 2.5 and l["nivel"] == "Verde", l
    esperado(http("POST", f"/api/sensors/{C['sen_r']}/value", token=C["operador"], cuerpo={"valor": 50}), 400, "entre 0 y 8")


@prueba("Sensor")
def sen_estado():
    """Desactivar un sensor; un inactivo no admite valores; 'SinSenal' no se asigna a mano"""
    s = esperado(http("PATCH", f"/api/sensors/{C['sen_r']}/status", token=C["operador"], cuerpo={"estado": "Inactivo"}), 200).json
    assert s["estado"] == "Inactivo"
    esperado(http("POST", f"/api/sensors/{C['sen_r']}/value", token=C["operador"], cuerpo={"valor": 2}), 400, "desactivado")
    esperado(http("PATCH", f"/api/sensors/{C['sen_r']}/status", token=C["operador"], cuerpo={"estado": "SinSenal"}), 400)
    esperado(http("PATCH", f"/api/sensors/{C['sen_r']}/status", token=C["operador"], cuerpo={"estado": "Activo"}), 200)


@prueba("Sensor")
def sen_actualizar():
    """Actualizar nombre y ubicación"""
    s = esperado(http("PUT", f"/api/sensors/{C['sen_r']}", token=C["admin"],
                      cuerpo={"nombre": "E2E Limnímetro", "ubicacion": "Puente"}), 200).json
    assert s["nombre"] == "E2E Limnímetro" and s["ubicacion"] == "Puente"


@prueba("Sensor")
def sen_lecturas_manuales():
    """Registrar y eliminar una lectura manual; paginación y filtros"""
    l = esperado(http("POST", "/api/sensors/readings", token=C["operador"], cuerpo={"sensorId": C["sen_r"], "valor": 1.25}), 200).json
    esperado(http("DELETE", f"/api/sensors/readings/{l['id']}", token=C["admin"]), 204)
    p = esperado(http("GET", f"/api/sensors/readings?comunidadId={C['com_a']}&tipo=NivelRio&pagina=1&tamano=2", token=C["consulta"]), 200).json
    assert p["tamanoPagina"] == 2 and all(x["tipo"] == "NivelRio" for x in p["elementos"]), p
    esperado(http("GET", "/api/sensors/readings?desde=2026-10-22T00:00:00Z&hasta=2026-10-01T00:00:00Z", token=C["consulta"]), 400)


@prueba("Sensor")
def sen_tablero():
    """Resumen, estado de la comunidad (con alertas de Alert) y series"""
    resumen = esperado(http("GET", f"/api/sensors/summary?comunidadId={C['com_a']}", token=C["consulta"]), 200).json
    assert resumen["totalSensores"] == 2, resumen
    estado = esperado(http("GET", f"/api/sensors/communities/{C['com_a']}/state", token=C["consulta"]), 200).json
    assert estado["alertasDisponibles"] is True and len(estado["sensores"]) == 2, estado
    series = esperado(http("GET", f"/api/sensors/communities/{C['com_a']}/series?minutos=5", token=C["consulta"]), 200).json
    assert {s["sensorId"] for s in series} == {C["sen_t"], C["sen_r"]}


@prueba("Sensor")
def sen_estadisticas():
    """Estadísticas de lecturas por tipo"""
    e = esperado(http("GET", f"/api/sensors/readings/stats?comunidadId={C['com_a']}", token=C["consulta"]), 200).json
    assert {x["tipo"] for x in e} >= {"Temperatura"}, e


@prueba("Sensor")
def sen_permisos():
    """Un Consulta no opera sensores; un Operador no los crea → 403"""
    esperado(http("POST", f"/api/sensors/{C['sen_t']}/value", token=C["consulta"], cuerpo={"valor": 20}), 403)
    esperado(http("POST", "/api/sensors", token=C["operador"], cuerpo={}), 403)
    esperado(http("POST", "/api/sensors/system/reset", token=C["operador"]), 403)


# =============================================================================================
# Alert
# =============================================================================================


@prueba("Alert")
def ale_helada():
    """Forzar −5 °C abre una helada ROJA con sensor, umbral y comunidad copiados"""
    l = esperado(http("POST", f"/api/sensors/{C['sen_h']}/value", token=C["operador"], cuerpo={"valor": -5}), 200).json
    assert l["nivel"] == "Rojo", l
    activas = esperado(http("GET", f"/api/alerts/active?comunidadId={C['com_b']}", token=C["consulta"]), 200).json
    a = next(a for a in activas["alertas"] if a["fenomeno"] == "Helada")
    assert activas["nivelGlobal"] == "Rojo" and a["nivel"] == "Rojo" and a["sensorCodigo"] == f"E2E-H{SUFIJO}", a
    assert a["umbral"] == 0 and a["comunidadNombre"].startswith("E2E Caserío") and "EMERGENCIA por helada" in a["mensaje"], a
    C["alerta"] = a["id"]


@prueba("Alert")
def ale_atender_cerrar():
    """Atender y cerrar; repetir → 409; un Consulta no atiende → 403"""
    esperado(http("POST", f"/api/alerts/{C['alerta']}/attend", token=C["consulta"]), 403)
    a = esperado(http("POST", f"/api/alerts/{C['alerta']}/attend", token=C["operador"], cuerpo={"comentario": "En camino"}), 200).json
    assert a["estado"] == "Atendida" and a["reconocidaPor"] == "E2E Operador", a
    esperado(http("POST", f"/api/alerts/{C['alerta']}/attend", token=C["operador"]), 409, "ya fue atendida")
    a = esperado(http("POST", f"/api/alerts/{C['alerta']}/close", token=C["operador"]), 200).json
    assert a["estado"] == "Cerrada" and a["activa"] is False and a["cerradaPor"] == "E2E Operador", a
    esperado(http("POST", f"/api/alerts/{C['alerta']}/close", token=C["operador"]), 409, "ya está cerrada")


@prueba("Alert")
def ale_reabre():
    """Si el riesgo persiste tras un cierre manual, el siguiente ciclo abre una alerta nueva"""
    def nueva():
        activas = http("GET", f"/api/alerts/active?comunidadId={C['com_b']}", token=C["consulta"]).json["alertas"]
        return next((a for a in activas if a["fenomeno"] == "Helada" and a["id"] != C["alerta"]), None)
    http("POST", f"/api/sensors/{C['sen_h']}/value", token=C["operador"], cuerpo={"valor": -6})
    C["alerta2"] = esperar(nueva, 15, mensaje="una helada nueva")["id"]


@prueba("Alert")
def ale_historial():
    """Historial de eventos: la helada cerrada a mano tiene responsable y duración"""
    eventos = esperado(http("GET", f"/api/alerts/events?comunidadId={C['com_b']}", token=C["consulta"]), 200).json["elementos"]
    cerrado = next(e for e in eventos if e["alertaId"] == C["alerta"])
    assert cerrado["estado"] == "Cerrada" and cerrado["usuarioResponsable"] == "E2E Operador" and cerrado["duracionMinutos"] is not None
    stats = esperado(http("GET", f"/api/alerts/events/stats?comunidadId={C['com_b']}", token=C["consulta"]), 200).json
    assert stats["totalEventos"] >= 2 and "Helada" in stats["porFenomeno"], stats


@prueba("Alert")
def ale_listado():
    """Listado con filtros, resumen y errores de parámetros en español"""
    p = esperado(http("GET", f"/api/alerts?comunidadId={C['com_b']}&fenomeno=Helada&tamano=5", token=C["consulta"]), 200).json
    assert p["totalElementos"] >= 2 and all(a["fenomeno"] == "Helada" for a in p["elementos"])
    resumen = esperado(http("GET", f"/api/alerts/summary?comunidadId={C['com_b']}", token=C["consulta"]), 200).json
    assert resumen["alertasActivas"] >= 1 and resumen["alertasPeriodoPorFenomeno"].get("Helada", 0) >= 2, resumen
    esperado(http("GET", "/api/alerts?nivel=Morado", token=C["consulta"]), 400, "'nivel'")
    esperado(http("GET", "/api/alerts?desde=ayer", token=C["consulta"]), 400, "ISO 8601")


@prueba("Alert")
def ale_reglas():
    """Reglas: crear, duplicada (409), sin rango (400), desactivar y eliminar; Consulta → 403"""
    cuerpo = {"nombre": f"E2E Humo {SUFIJO}", "tipoSensor": "Humo", "valorMinimo": 450, "nivel": "Rojo",
              "fenomeno": "IncendioForestal", "mensaje": "{sensor}: {valor} {unidad}"}
    esperado(http("POST", "/api/alerts/rules", token=C["consulta"], cuerpo=cuerpo), 403)
    r = esperado(http("POST", "/api/alerts/rules", token=C["admin"], cuerpo=cuerpo), 201).json
    esperado(http("POST", "/api/alerts/rules", token=C["admin"], cuerpo=cuerpo), 409)
    esperado(http("POST", "/api/alerts/rules", token=C["admin"], cuerpo={**cuerpo, "nombre": "x", "valorMinimo": None}), 400, "al menos")
    assert esperado(http("PATCH", f"/api/alerts/rules/{r['id']}/status", token=C["admin"], cuerpo={"activa": False}), 200).json["activa"] is False
    esperado(http("DELETE", f"/api/alerts/rules/{r['id']}", token=C["admin"]), 204)
    esperado(http("GET", f"/api/alerts/rules/{r['id']}", token=C["admin"]), 404)


@prueba("Alert")
def ale_umbrales():
    """Recalibrar umbrales cambia el nivel del sensor en el siguiente ciclo; escalera inválida → 400"""
    esperado(http("PUT", f"/api/alerts/thresholds/{C['sen_t']}", token=C["admin"],
                  cuerpo={"tipoSensor": "Temperatura", "umbralAmarilloAlto": 30, "umbralNaranjaAlto": 20, "umbralRojoAlto": 40}), 400)
    u = esperado(http("PUT", f"/api/alerts/thresholds/{C['sen_t']}", token=C["admin"], cuerpo={
        "tipoSensor": "Temperatura", "umbralAmarilloAlto": -9.8, "umbralNaranjaAlto": -9.7, "umbralRojoAlto": -9.6}), 200).json
    assert u["personalizados"] is True
    esperar(lambda: http("GET", f"/api/sensors/{C['sen_t']}", token=C["consulta"]).json["nivelActual"] == "Rojo",
            15, mensaje="que el sensor pase a Rojo con los umbrales nuevos")
    esperado(http("DELETE", f"/api/alerts/thresholds/{C['sen_t']}", token=C["admin"]), 204)
    p = esperado(http("GET", f"/api/alerts/thresholds/{C['sen_t']}?tipo=Temperatura", token=C["consulta"]), 200).json
    assert p["personalizados"] is False and p["umbralRojoAlto"] == 42, p


@prueba("Alert")
def ale_comunidad_inactiva():
    """Desactivar la comunidad deja de simular sus sensores y cierra sus alertas"""
    esperado(http("PATCH", f"/api/communities/{C['com_b']}/status", token=C["admin"], cuerpo={"activa": False}), 200)
    esperar(lambda: not http("GET", f"/api/alerts/active?comunidadId={C['com_b']}", token=C["consulta"]).json["alertas"],
            30, mensaje="que se cierren las alertas de la comunidad inactiva")
    antes = http("GET", f"/api/sensors/readings?sensorId={C['sen_h']}", token=C["consulta"]).json["totalElementos"]
    time.sleep(7)
    despues = http("GET", f"/api/sensors/readings?sensorId={C['sen_h']}", token=C["consulta"]).json["totalElementos"]
    assert despues == antes, f"siguió generando lecturas: {antes} → {despues}"
    esperado(http("POST", f"/api/sensors/{C['sen_h']}/value", token=C["operador"], cuerpo={"valor": 10}), 400, "desactivada")


# =============================================================================================
# Tiempo real (SignalR por el gateway)
# =============================================================================================


@prueba("Tiempo real")
def tr_signalr():
    """WebSocket por el gateway: EstadoComunidad, LecturaRecibida y AlertaGenerada al forzar un valor"""
    if not shutil.which("node") or not (AQUI / "node_modules" / "@microsoft" / "signalr").exists():
        raise Omitida("ejecute 'npm install' en tools/pruebas")
    proceso = subprocess.Popen(["node", str(AQUI / "hub.mjs"), GATEWAY, C["admin"], str(C["com_a"]), "9"],
                               stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8")
    time.sleep(3)
    http("POST", f"/api/sensors/{C['sen_r']}/value", token=C["operador"], cuerpo={"valor": 4.9})  # inundación roja
    salida, error = proceso.communicate(timeout=30)
    datos = json.loads(salida.strip().splitlines()[-1])
    assert datos["conectado"] and datos["ping"] and not datos["error"], datos | {"stderr": error}
    assert datos["estado"]["sensores"] == 2, datos
    assert datos["recibidos"].get("LecturaRecibida", 0) >= 2, datos
    assert datos["recibidos"].get("AlertaGenerada", 0) >= 1, datos


@prueba("Tiempo real")
def tr_signalr_sin_token():
    """El hub rechaza un token inválido"""
    if not shutil.which("node") or not (AQUI / "node_modules" / "@microsoft" / "signalr").exists():
        raise Omitida("ejecute 'npm install' en tools/pruebas")
    salida = subprocess.run(["node", str(AQUI / "hub.mjs"), GATEWAY, "x.y.z", "1", "1"],
                            capture_output=True, text=True, encoding="utf-8", timeout=30).stdout
    datos = json.loads(salida.strip().splitlines()[-1])
    assert not datos["conectado"] and datos["error"], datos


# =============================================================================================
# Report
# =============================================================================================


def generar(tipo: str, filtros: dict, token: str) -> dict:
    r = esperado(http("POST", "/api/reports", token=token, cuerpo={"type": tipo, "filters": filtros}), 202).json
    assert r["status"] == "PENDING" and r["reportId"].startswith("REP-"), r
    return esperar(lambda: (d := http("GET", f"/api/reports/{r['reportId']}", token=token).json)["status"]
                   in ("COMPLETED", "FAILED") and d, 60, 0.5, f"que termine {r['reportId']}")


@prueba("Report")
def rep_tipos():
    """Catálogo con los 8 reportes RF-REP-01…08"""
    tipos = esperado(http("GET", "/api/reports/types", token=C["operador"]), 200).json
    assert [t["requirement"] for t in tipos] == [f"RF-REP-0{i}" for i in range(1, 9)]


@prueba("Report")
def rep_todos():
    """Generar los 8 reportes y descargar cada PDF"""
    filtros = {"READINGS": {"comunidadId": C["com_a"]}, "ALERTS": {"comunidadId": C["com_b"]},
               "EVENT_HISTORY": {"comunidadId": C["com_b"]}, "SENSOR_STATUS": {"comunidadId": C["com_a"]}}
    C["reportes"] = {}
    for tipo in ("COMMUNITIES", "SENSORS", "READINGS", "ALERTS", "EVENT_HISTORY", "SENSOR_STATUS", "CLIMATE_STATS", "EXECUTIVE"):
        d = generar(tipo, filtros.get(tipo, {}), C["admin"])
        assert d["status"] == "COMPLETED", d
        pdf = http("GET", f"/api/reports/{d['reportId']}/download", token=C["admin"])
        assert pdf.estado == 200 and pdf.cuerpo.startswith(b"%PDF") and len(pdf.cuerpo) > 5000, (tipo, pdf.estado, len(pdf.cuerpo))
        assert d["fileName"] in pdf.cabeceras["content-disposition"]
        C["reportes"][tipo] = d["reportId"]


@prueba("Report")
def rep_permisos():
    """Operador genera pero no descarga (RF-REP-10); Consulta no genera; ajeno → 404"""
    d = generar("SENSORS", {"comunidadId": C["com_a"]}, C["operador"])
    esperado(http("GET", f"/api/reports/{d['reportId']}/download", token=C["operador"]), 403)
    esperado(http("POST", "/api/reports", token=C["consulta"], cuerpo={"type": "SENSORS"}), 403)
    esperado(http("GET", f"/api/reports/{C['reportes']['COMMUNITIES']}", token=C["operador"]), 404)
    propios = http("GET", "/api/reports", token=C["operador"]).json
    assert all(r["generatedBy"]["email"] == C["email_Operador"] for r in propios["elementos"]), propios


@prueba("Report")
def rep_historial():
    """Historial (RF-REP-09) con metadata de la sección 18 y descarga registrada"""
    d = http("GET", f"/api/reports/{C['reportes']['ALERTS']}", token=C["admin"]).json
    for clave in ("reportId", "type", "generatedBy", "generatedAt", "filters", "status", "fileName"):
        assert clave in d, clave
    assert d["filters"] == {"comunidadId": C["com_b"]} and d["downloadCount"] == 1 and d["lastDownloadAt"], d
    assert http("GET", "/api/reports?type=ALERTS&status=COMPLETED", token=C["admin"]).json["totalElementos"] >= 1


@prueba("Report")
def rep_validaciones():
    """Tipo desconocido, filtro no admitido y valor inválido → 400"""
    esperado(http("POST", "/api/reports", token=C["admin"], cuerpo={"type": "VOLCANES"}), 400, "desconocido")
    esperado(http("POST", "/api/reports", token=C["admin"], cuerpo={"type": "COMMUNITIES", "filters": {"nivel": "Rojo"}}), 400, "no admite")
    esperado(http("POST", "/api/reports", token=C["admin"], cuerpo={"type": "ALERTS", "filters": {"fenomeno": "Volcan"}}), 400, "debe ser uno de")


@prueba("Report")
def rep_dashboard():
    """Dashboard estadístico con los tres servicios disponibles"""
    d = esperado(http("GET", "/api/reports/dashboard", token=C["consulta"]), 200).json
    assert d["disponibles"] == {"community": True, "sensor": True, "alert": True}, d["disponibles"]
    assert d["sensores"]["total"] >= 3 and d["comunidades"]["total"] >= 2 and d["alertas"] is not None


# =============================================================================================
# Audit (al final: verifica lo que registraron los demás)
# =============================================================================================


@prueba("Audit")
def aud_eventos_de_cada_servicio():
    """Cada servicio registró sus acciones en la bitácora"""
    time.sleep(3)  # los envíos son por lotes, cada segundo
    esperadas = {
        "auth-service": "UsuarioCreado", "community-service": "ComunidadCreada", "sensor-service": "SensorCreado",
        "alert-service": "AlertaAtendida", "report-service": "ReporteDescargado",
    }
    for servicio, accion in esperadas.items():
        p = esperado(http("GET", f"/api/audit/events?servicio={servicio}&accion={accion}&tamano=1", token=C["admin"]), 200).json
        assert p["totalElementos"] >= 1, f"{servicio} no registró {accion}"


@prueba("Audit")
def aud_filtros_y_catalogos():
    """Filtro por usuario y catálogos; Consulta no ve la bitácora → 403"""
    p = esperado(http("GET", f"/api/audit/events?usuario={C['email_Operador']}", token=C["admin"]), 200).json
    assert p["totalElementos"] >= 3 and all(e["usuarioEmail"] == C["email_Operador"] for e in p["elementos"])
    catalogos = esperado(http("GET", "/api/audit/catalogs", token=C["admin"]), 200).json
    assert {"auth-service", "sensor-service", "alert-service", "report-service", "community-service"} <= set(catalogos["servicios"])
    esperado(http("GET", "/api/audit/events", token=C["consulta"]), 403)


@prueba("Gateway")
def gw_limite_login():
    """Límite de 10 intentos de login por minuto → 429 (va al final: bloquea el login un minuto)"""
    estados = [http("POST", "/api/auth/login", cuerpo={"email": "nadie@ejemplo.org", "password": "xxxxxxx"}).estado for _ in range(12)]
    assert 429 in estados, estados


# =============================================================================================
# Ejecución
# =============================================================================================


class Omitida(Exception):
    pass


def limpiar():
    """Desactiva lo creado: las comunidades quedan inactivas (no se simulan) y los usuarios, deshabilitados."""
    for clave in ("sen_t", "sen_r", "sen_h"):
        if clave in C:
            http("DELETE", f"/api/sensors/{C[clave]}", token=C["admin"])
    for clave in ("com_a", "com_b"):
        if clave in C:
            http("PATCH", f"/api/communities/{C[clave]}/status", token=C["admin"], cuerpo={"activa": False})
    for rol in ("Operador", "Consulta"):
        if f"id_{rol}" in C:
            http("PATCH", f"/api/auth/users/{C[f'id_{rol}']}/status", token=C["admin"], cuerpo={"activo": False})


def main() -> int:
    print(f"SWMATRC · pruebas de punta a punta contra {GATEWAY} (sufijo {SUFIJO})\n")
    if not ADMIN_PASSWORD:
        print("Falta ADMIN_PASSWORD (en .env o en el entorno).")
        return 2
    C["admin"] = token_de(ADMIN_EMAIL, ADMIN_PASSWORD)

    resultados = {"ok": 0, "falla": 0, "omitida": 0}
    seccion_actual = None
    inicio = time.time()
    for seccion, nombre, funcion in PRUEBAS:
        if seccion != seccion_actual:
            print(f"\n■ {seccion}")
            seccion_actual = seccion
        t0 = time.time()
        try:
            funcion()
            resultados["ok"] += 1
            print(f"  ✔ {nombre} ({time.time() - t0:.1f} s)")
        except Omitida as e:
            resultados["omitida"] += 1
            print(f"  ○ {nombre} — omitida: {e}")
        except Exception as e:
            resultados["falla"] += 1
            print(f"  ✘ {nombre}\n      {type(e).__name__}: {e}")
            if os.environ.get("DETALLE"):
                traceback.print_exc()

    limpiar()
    total = sum(resultados.values())
    print(f"\n{resultados['ok']}/{total} correctas, {resultados['falla']} con error, "
          f"{resultados['omitida']} omitidas · {time.time() - inicio:.0f} s")
    return 1 if resultados["falla"] else 0


if __name__ == "__main__":
    sys.exit(main())

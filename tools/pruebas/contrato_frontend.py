"""
Contrato del frontend: ejecuta, a través del contenedor web (nginx → gateway), cada llamada
que hace ``frontend/src/app/core/servicios/api.ts`` con el mismo método, ruta y cuerpo, y
comprueba que la respuesta tiene los campos que las páginas leen. No reemplaza revisar la
interfaz en un navegador, pero asegura que ninguna pantalla se quede sin datos por una ruta
o un campo equivocado.

    python tools/pruebas/contrato_frontend.py            # contra http://127.0.0.1:10001
"""

import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
WEB = os.environ.get("WEB", "http://127.0.0.1:10001").rstrip("/")
RAIZ = Path(__file__).resolve().parents[2]
ENV = dict(l.split("=", 1) for l in (RAIZ / ".env").read_text(encoding="utf-8").splitlines() if "=" in l and not l.startswith("#"))


def http(metodo, ruta, token=None, cuerpo=None, binario=False):
    datos = None if cuerpo is None else json.dumps(cuerpo).encode()
    p = urllib.request.Request(WEB + ruta, data=datos, method=metodo)
    if datos is not None:
        p.add_header("Content-Type", "application/json")
    if token:
        p.add_header("Authorization", f"Bearer {token}")
    try:
        with urllib.request.urlopen(p, timeout=30) as r:
            contenido = r.read()
            return r.status, (contenido if binario else (json.loads(contenido) if contenido else None))
    except urllib.error.HTTPError as e:
        cuerpo = e.read()
        try:
            return e.code, json.loads(cuerpo)
        except Exception:
            return e.code, cuerpo


resultados = []


def comprobar(nombre, condicion, detalle=""):
    resultados.append(bool(condicion))
    print(f"  {'✔' if condicion else '✘'} {nombre}" + ("" if condicion else f"  → {detalle}"))


def campos(obj, nombres):
    faltan = [n for n in nombres if n not in obj]
    return not faltan, f"faltan {faltan}"


print(f"Contrato del frontend contra {WEB}\n")

# --- La SPA servida ---------------------------------------------------------------------------
estado, indice = http("GET", "/", binario=True)
comprobar("index.html servido", estado == 200 and b"<app-root" in indice)
bundles = re.findall(rb'src="(main-[^"]+\.js)"', indice)
codigo = b""
for b in bundles:
    codigo += http("GET", "/" + b.decode(), binario=True)[1]
for chunk in set(re.findall(rb'"\./(chunk-[A-Z0-9]+\.js)"', codigo)):
    codigo += http("GET", "/" + chunk.decode(), binario=True)[1]
comprobar("el paquete usa /auth/login y /hubs/monitoring", b"/auth/login" in codigo and b"/hubs/monitoring" in codigo)
comprobar("el paquete ya no usa rutas de la Fase 2", b"/cuenta/login" not in codigo and b"/monitoreo/comunidades" not in codigo)

# --- Login y sesión (autenticacion.ts) --------------------------------------------------------
estado, sesion = http("POST", "/api/auth/login", cuerpo={"email": ENV["ADMIN_EMAIL"], "password": ENV["ADMIN_PASSWORD"]})
comprobar("login: token, expira y usuario con rol y permisos", estado == 200 and sesion["token"] and sesion["expira"]
          and campos(sesion["usuario"], ["id", "nombreCompleto", "email", "rol", "rolNombre", "permisos", "activo"])[0], sesion)
T = sesion["token"]

# --- Monitoreo (estado-monitoreo.ts) ----------------------------------------------------------
estado, comunidades = http("GET", "/api/communities", T)
comprobar("comunidades", estado == 200 and comunidades and campos(comunidades[0], ["id", "nombre", "activa", "totalSensores", "latitud"])[0])
cid = next(c["id"] for c in comunidades if c["activa"])
estado, e = http("GET", f"/api/sensors/communities/{cid}/state", T)
comprobar("estado de comunidad: sensores con nivelActual y alertasActivas", estado == 200 and "alertasActivas" in e
          and all(campos(s, ["id", "tipo", "valorActual", "nivelActual", "estado"])[0] for s in e["sensores"]), e)
estado, series = http("GET", f"/api/sensors/communities/{cid}/series?minutos=30", T)
comprobar("series", estado == 200 and all(campos(s, ["sensorId", "puntos", "unidadMedida"])[0] for s in series))
estado, d = http("GET", f"/api/reports/dashboard?comunidadId={cid}", T)
comprobar("resumen del tablero (Report Service)", estado == 200 and d["sensores"] and d["alertas"] and d["comunidades"]
          and campos(d["alertas"], ["alertasActivas", "eventosUltimas24h", "alertasPeriodoPorNivel", "alertasPeriodoPorFenomeno"])[0], d)

# --- Sensores (sensores.ts) -------------------------------------------------------------------
estado, sensores = http("GET", f"/api/sensors?comunidadId={cid}", T)
comprobar("sensores", estado == 200 and sensores)
estado, calibrados = http("GET", "/api/alerts/thresholds", T)
comprobar("umbrales calibrados", estado == 200 and isinstance(calibrados, list))
estado, plantillasU = http("GET", "/api/alerts/thresholds/templates", T)
comprobar("plantillas de umbrales", estado == 200 and "Temperatura" in plantillasU and "rojoAlto" in plantillasU["Temperatura"])
estado, plantillasF = http("GET", "/api/sensors/templates", T)
comprobar("plantillas físicas con tipoValor", estado == 200 and campos(plantillasF[0], ["tipo", "tipoValor", "unidad", "valorReposo"])[0])

sufijo = time.strftime("%H%M%S")
estado, nuevo = http("POST", "/api/sensors", T, {"comunidadId": cid, "codigo": f"fe-{sufijo}", "nombre": "Prueba frontend",
                                                 "tipo": "Lluvia", "valorMinimo": 0, "valorMaximo": 120, "variacionMaxima": 2})
comprobar("crear sensor (tipo como texto)", estado == 201, nuevo)
sid = nuevo["id"]
estado, u = http("PUT", f"/api/alerts/thresholds/{sid}", T, {"tipoSensor": "Lluvia", "umbralAmarilloAlto": 20, "umbralNaranjaAlto": 40,
                                                            "umbralRojoAlto": 70, "umbralAmarilloBajo": None, "umbralNaranjaBajo": None, "umbralRojoBajo": None})
comprobar("guardar umbrales del sensor", estado == 200 and u["personalizados"], u)
estado, _ = http("PUT", f"/api/sensors/{sid}", T, {"comunidadId": cid, "nombre": "Prueba frontend 2", "unidadMedida": "mm"})
comprobar("actualizar sensor", estado == 200)
estado, s = http("PATCH", f"/api/sensors/{sid}/status", T, {"estado": "Inactivo"})
comprobar("cambiar estado (cuerpo {estado})", estado == 200 and s["estado"] == "Inactivo", s)
http("PATCH", f"/api/sensors/{sid}/status", T, {"estado": "Activo"})
estado, l = http("POST", f"/api/sensors/{sid}/value", T, {"valor": 12.5})
comprobar("establecer valor", estado == 200 and l["valor"] == 12.5, l)

# --- Lecturas (lecturas.ts) -------------------------------------------------------------------
estado, p = http("GET", f"/api/sensors/readings?comunidadId={cid}&pagina=1&tamano=20", T)
comprobar("lecturas paginadas", estado == 200 and campos(p, ["elementos", "pagina", "tamanoPagina", "totalElementos", "totalPaginas"])[0])
estado, l = http("POST", "/api/sensors/readings", T, {"sensorId": sid, "valor": 3})
comprobar("registrar lectura", estado == 200, l)
estado, _ = http("DELETE", f"/api/sensors/readings/{l['id']}", T)
comprobar("eliminar lectura", estado == 204)
estado, _ = http("DELETE", f"/api/sensors/{sid}", T)
http("DELETE", f"/api/alerts/thresholds/{sid}", T)
comprobar("eliminar sensor y sus umbrales", estado == 204)

# --- Alertas, historial y reglas --------------------------------------------------------------
estado, a = http("GET", "/api/alerts/active", T)
comprobar("alertas activas ({nivelGlobal, alertas})", estado == 200 and "alertas" in a)
estado, p = http("GET", "/api/alerts?pagina=1&tamano=10", T)
comprobar("alertas paginadas", estado == 200 and "elementos" in p)
if p["elementos"]:
    estado, una = http("GET", f"/api/alerts/{p['elementos'][0]['id']}", T)
    comprobar("detalle de alerta", estado == 200 and campos(una, ["fenomenoNombre", "nivel", "umbral", "unidadMedida", "reconocidaPor"])[0], una)
estado, p = http("GET", "/api/alerts/events?pagina=1&tamano=10", T)
comprobar("historial de eventos", estado == 200 and "elementos" in p)
estado, st = http("GET", "/api/alerts/events/stats", T)
comprobar("estadísticas de eventos", estado == 200 and campos(st, ["totalEventos", "porFenomeno", "porNivel", "porComunidad", "porDia"])[0])
estado, reglas = http("GET", "/api/alerts/rules", T)
comprobar("reglas", estado == 200 and reglas and campos(reglas[0], ["id", "nombre", "tipoSensor", "nivel", "fenomeno", "activa"])[0])
estado, r = http("PATCH", f"/api/alerts/rules/{reglas[0]['id']}/status", T, {"activa": reglas[0]["activa"]})
comprobar("cambiar estado de regla (cuerpo {activa})", estado == 200)

# --- Comunidades, usuarios y bitácora ---------------------------------------------------------
estado, c = http("PATCH", f"/api/communities/{cid}/status", T, {"activa": True})
comprobar("cambiar estado de comunidad (cuerpo {activa})", estado == 200 and c["activa"])
estado, usuarios = http("GET", "/api/auth/users", T)
comprobar("usuarios", estado == 200 and campos(usuarios[0], ["id", "nombreCompleto", "email", "rol", "rolNombre", "activo"])[0])
estado, b = http("GET", "/api/audit/events?pagina=1&tamano=10", T)
comprobar("bitácora", estado == 200 and campos(b["elementos"][0], ["usuarioEmail", "accion", "entidad", "detalle", "fechaHora"])[0])
estado, cat = http("GET", "/api/audit/catalogs", T)
comprobar("catálogos de bitácora", estado == 200 and campos(cat, ["acciones", "entidades"])[0])

# --- Reportes (reportes.ts) -------------------------------------------------------------------
estado, tipos = http("GET", "/api/reports/types", T)
comprobar("tipos de reporte", estado == 200 and len(tipos) == 8 and campos(tipos[0], ["type", "name", "filters"])[0])
estado, rep = http("POST", "/api/reports", T, {"type": "SENSOR_STATUS", "filters": {"comunidadId": cid}})
comprobar("solicitar reporte", estado == 202 and rep["status"] == "PENDING", rep)
for _ in range(40):
    _, rep = http("GET", f"/api/reports/{rep['reportId']}", T)
    if rep["status"] in ("COMPLETED", "FAILED"):
        break
    time.sleep(0.5)
comprobar("el reporte se completa", rep["status"] == "COMPLETED" and campos(rep, ["statusLabel", "rows", "fileSize", "typeName"])[0], rep)
estado, pdf = http("GET", f"/api/reports/{rep['reportId']}/download", T, binario=True)
comprobar("descargar PDF (blob)", estado == 200 and pdf[:4] == b"%PDF")
estado, h = http("GET", "/api/reports?pagina=1&tamano=15", T)
comprobar("historial de reportes", estado == 200 and h["elementos"] and campos(h["elementos"][0], ["reportId", "generatedBy", "filters", "lastDownloadAt"])[0])

# --- Logout -----------------------------------------------------------------------------------
estado, _ = http("POST", "/api/auth/logout", T)
comprobar("logout", estado == 204)

print(f"\n{sum(resultados)}/{len(resultados)} correctas")
sys.exit(0 if all(resultados) else 1)

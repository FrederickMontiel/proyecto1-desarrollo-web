"""
Migración de datos de la Fase 2 (monolito, SQL Server ``SwmatrcDb``) a las bases de los
microservicios de la Fase 3.

    docker compose stop auth-service community-service sensor-service alert-service report-service audit-service
    docker compose --profile migracion run --rm migracion
    docker compose up -d

Qué hace, por servicio (conservando los id originales):

  * Auth (PostgreSQL ``auth_db``): usuarios con su hash BCrypt tal cual → las cuentas
    entran con la misma contraseña. El rol numérico pasa a la tabla ``roles``.
  * Community (PostgreSQL ``community_db``): comunidades.
  * Sensor (SQL Server ``sensor_db``): sensores y lecturas. Se copian dentro de la misma
    instancia con INSERT … SELECT (222 000 filas sin salir del motor). El nivel de cada
    lectura se calcula con los umbrales que tenía su sensor en la Fase 2.
  * Alert (PostgreSQL ``alert_db``): reglas, umbrales por sensor (antes vivían en el
    sensor), alertas y eventos, con copia del nombre de comunidad, código de sensor y
    nombre de los usuarios responsables.
  * Audit (MongoDB ``audit_db``): la bitácora, con servicio ``monolito-fase2``.
  * Report (MongoDB ``report_db``): se vacía (en la Fase 2 no había reportes).

Antes de importar VACÍA esas tablas y colecciones (los datos de prueba de la Fase 3). Por
eso exige ``--confirmar``. Es una herramienta de un solo uso: lee la base de origen con
el usuario ``sa`` y escribe en cada base destino con el usuario de su servicio.
"""

import json
import os
import sys
import time
from datetime import datetime, timezone

import psycopg
import pymssql
from pymongo import MongoClient

TIPOS_SENSOR = {1: "Temperatura", 2: "Humedad", 3: "Viento", 4: "Lluvia", 5: "NivelRio",
                6: "NivelReservorio", 7: "Humo", 8: "Otro"}
ROLES = {0: "Consulta", 1: "Operador", 2: "Administrador"}
LEGADO = os.environ.get("MSSQL_DATABASE_LEGADO", "SwmatrcDb")


def env(nombre: str) -> str:
    valor = os.environ.get(nombre)
    if not valor:
        sys.exit(f"Falta la variable de entorno {nombre}.")
    return valor


def utc(fecha: datetime | None) -> datetime | None:
    """La Fase 2 guardaba DateTime.UtcNow en datetime2, que no lleva zona: se marca como UTC."""
    return None if fecha is None else fecha.replace(tzinfo=timezone.utc)


def paso(texto: str) -> float:
    print(f"\n■ {texto}", flush=True)
    return time.perf_counter()


def listo(inicio: float, detalle: str) -> None:
    print(f"  ✔ {detalle} ({time.perf_counter() - inicio:.1f} s)", flush=True)


def postgres(base: str, usuario: str, password: str):
    return psycopg.connect(host="postgres", dbname=base, user=usuario, password=password, autocommit=False)


def reiniciar_identidad(cur, tabla: str) -> None:
    """Tras insertar con id explícitos, la secuencia sigue en 1: se adelanta al máximo."""
    cur.execute(f"SELECT setval(pg_get_serial_sequence('{tabla}', 'id'), GREATEST(COALESCE(MAX(id), 0), 1), MAX(id) IS NOT NULL) FROM {tabla}")


def main() -> None:
    if "--confirmar" not in sys.argv:
        sys.exit("Esta herramienta vacía las bases de la Fase 3 antes de importar. Ejecútela con --confirmar.")

    sa = env("MSSQL_SA_PASSWORD")
    origen = pymssql.connect(server="sqlserver", user="sa", password=sa, database=LEGADO, as_dict=True, autocommit=True)
    o = origen.cursor()

    def leer(sql: str) -> list[dict]:
        o.execute(sql)
        return o.fetchall()

    conteos: dict[str, tuple[int, int]] = {}

    # Nombres que las alertas y eventos guardan como copia (en la Fase 3 viven en otros servicios).
    comunidades = {c["Id"]: c for c in leer("SELECT * FROM Comunidades")}
    sensores = {s["Id"]: s for s in leer("SELECT * FROM Sensores")}
    usuarios = {u["Id"]: u for u in leer("SELECT * FROM Usuarios")}

    # ------------------------------------------------------------------------------------
    t = paso("Auth Service · usuarios (auth_db)")
    with postgres("auth_db", "auth_svc", env("AUTH_DB_PASSWORD")) as pg, pg.cursor() as cur:
        cur.execute("SELECT codigo, id FROM roles")
        roles = dict(cur.fetchall())
        if set(roles) != set(ROLES.values()):
            sys.exit("auth_db no tiene los roles esperados: arranque el Auth Service una vez antes de migrar.")
        cur.execute("TRUNCATE sesiones, identidades_externas, usuarios RESTART IDENTITY CASCADE")
        cur.executemany(
            """INSERT INTO usuarios (id, nombre_completo, email, password_hash, rol_id, activo, ultimo_acceso,
                                     fecha_creacion, fecha_modificacion)
               OVERRIDING SYSTEM VALUE VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s)""",
            [(u["Id"], u["NombreCompleto"], u["Email"].strip().lower(), u["PasswordHash"], roles[ROLES[u["Rol"]]],
              bool(u["Activo"]), utc(u["UltimoAcceso"]), utc(u["FechaCreacion"]), utc(u["FechaModificacion"]))
             for u in usuarios.values()])
        reiniciar_identidad(cur, "usuarios")
        cur.execute("SELECT COUNT(*) FROM usuarios")
        conteos["Usuarios"] = (len(usuarios), cur.fetchone()[0])
    listo(t, f"{conteos['Usuarios'][1]} usuarios (contraseñas BCrypt conservadas)")

    # ------------------------------------------------------------------------------------
    t = paso("Community Service · comunidades (community_db)")
    with postgres("community_db", "community_svc", env("COMMUNITY_DB_PASSWORD")) as pg, pg.cursor() as cur:
        cur.execute("TRUNCATE comunidades RESTART IDENTITY")
        cur.executemany(
            """INSERT INTO comunidades (id, nombre, municipio, departamento, pais, descripcion, latitud, longitud,
                                        poblacion, activa, fecha_creacion, fecha_modificacion)
               OVERRIDING SYSTEM VALUE VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)""",
            [(c["Id"], c["Nombre"], c["Municipio"] or "", c["Departamento"] or "", c["Pais"] or "", c["Descripcion"],
              c["Latitud"], c["Longitud"], c["Poblacion"], bool(c["Activa"]), utc(c["FechaCreacion"]),
              utc(c["FechaModificacion"])) for c in comunidades.values()])
        reiniciar_identidad(cur, "comunidades")
        cur.execute("SELECT COUNT(*) FROM comunidades")
        conteos["Comunidades"] = (len(comunidades), cur.fetchone()[0])
    listo(t, f"{conteos['Comunidades'][1]} comunidades")

    # ------------------------------------------------------------------------------------
    t = paso("Sensor Service · sensores y lecturas (sensor_db)")
    # Nivel de una lectura según los umbrales que tenía su sensor (la Fase 2 lo calculaba al consultar).
    nivel = lambda v: f"""CASE
        WHEN (S.UmbralRojoAlto IS NOT NULL AND {v} >= S.UmbralRojoAlto) OR (S.UmbralRojoBajo IS NOT NULL AND {v} <= S.UmbralRojoBajo) THEN 3
        WHEN (S.UmbralNaranjaAlto IS NOT NULL AND {v} >= S.UmbralNaranjaAlto) OR (S.UmbralNaranjaBajo IS NOT NULL AND {v} <= S.UmbralNaranjaBajo) THEN 2
        WHEN (S.UmbralAmarilloAlto IS NOT NULL AND {v} >= S.UmbralAmarilloAlto) OR (S.UmbralAmarilloBajo IS NOT NULL AND {v} <= S.UmbralAmarilloBajo) THEN 1
        ELSE 0 END"""
    destino = pymssql.connect(server="sqlserver", user="sa", password=sa, database="sensor_db", autocommit=False)
    d = destino.cursor()
    d.execute("DELETE FROM Lecturas; DELETE FROM Sensores;")
    d.execute(f"""
        SET IDENTITY_INSERT dbo.Sensores ON;
        INSERT INTO dbo.Sensores (Id, ComunidadId, Codigo, Nombre, Tipo, UnidadMedida, Estado, Ubicacion, Descripcion,
               FechaInstalacion, Latitud, Longitud, ValorMinimo, ValorMaximo, VariacionMaxima, ValorActual, UltimaLectura,
               NivelActual, FechaCreacion, FechaModificacion)
        SELECT S.Id, S.ComunidadId, S.Codigo, S.Nombre, S.Tipo, S.UnidadMedida, S.Estado, S.Ubicacion, S.Descripcion,
               S.FechaInstalacion, S.Latitud, S.Longitud, S.ValorMinimo, S.ValorMaximo, S.VariacionMaxima, S.ValorActual,
               S.UltimaLectura, {nivel('S.ValorActual')}, S.FechaCreacion, S.FechaModificacion
        FROM {LEGADO}.dbo.Sensores S;
        SET IDENTITY_INSERT dbo.Sensores OFF;""")
    d.execute(f"""
        SET IDENTITY_INSERT dbo.Lecturas ON;
        INSERT INTO dbo.Lecturas (Id, SensorId, Valor, UnidadMedida, EstadoSensor, Nivel, FechaHora)
        SELECT L.Id, L.SensorId, L.Valor, L.UnidadMedida, L.EstadoSensor, {nivel('L.Valor')}, L.FechaHora
        FROM {LEGADO}.dbo.Lecturas L JOIN {LEGADO}.dbo.Sensores S ON S.Id = L.SensorId;
        SET IDENTITY_INSERT dbo.Lecturas OFF;""")
    destino.commit()
    d.execute("SELECT (SELECT COUNT(*) FROM Sensores), (SELECT COUNT(*) FROM Lecturas)")
    s_dest, l_dest = d.fetchone()
    lecturas_origen = leer("SELECT COUNT(*) n FROM Lecturas")[0]["n"]
    conteos["Sensores"] = (len(sensores), s_dest)
    conteos["Lecturas"] = (lecturas_origen, l_dest)
    destino.close()
    listo(t, f"{s_dest} sensores y {l_dest:,} lecturas con su nivel")

    # ------------------------------------------------------------------------------------
    t = paso("Alert Service · reglas, umbrales, alertas y eventos (alert_db)")
    nombre_usuario = lambda uid: (usuarios[uid]["NombreCompleto"] if uid in usuarios else None)
    with postgres("alert_db", "alert_svc", env("ALERT_DB_PASSWORD")) as pg, pg.cursor() as cur:
        cur.execute("TRUNCATE eventos_historial, alertas, umbrales_sensor, reglas_alerta RESTART IDENTITY")

        reglas = leer("SELECT * FROM ReglasAlerta")
        cur.executemany(
            """INSERT INTO reglas_alerta (id, nombre, tipo_sensor, valor_minimo, valor_maximo, nivel, fenomeno, mensaje,
                                          activa, fecha_creacion, fecha_modificacion)
               OVERRIDING SYSTEM VALUE VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)""",
            [(r["Id"], r["Nombre"], TIPOS_SENSOR[r["TipoSensor"]], r["ValorMinimo"], r["ValorMaximo"], r["Nivel"],
              r["Fenomeno"], r["Mensaje"], bool(r["Activa"]), utc(r["FechaCreacion"]), utc(r["FechaModificacion"]))
             for r in reglas])
        reiniciar_identidad(cur, "reglas_alerta")

        # En la Fase 2 cada sensor llevaba su escalera de umbrales: se conserva tal cual.
        cur.executemany(
            """INSERT INTO umbrales_sensor (sensor_id, tipo_sensor, amarillo_alto, naranja_alto, rojo_alto,
                                            amarillo_bajo, naranja_bajo, rojo_bajo, fecha_modificacion)
               VALUES (%s, %s, %s, %s, %s, %s, %s, %s, now())""",
            [(s["Id"], TIPOS_SENSOR[s["Tipo"]], s["UmbralAmarilloAlto"], s["UmbralNaranjaAlto"], s["UmbralRojoAlto"],
              s["UmbralAmarilloBajo"], s["UmbralNaranjaBajo"], s["UmbralRojoBajo"]) for s in sensores.values()])

        alertas = leer("SELECT * FROM Alertas ORDER BY Id")
        omitidas = 0
        for a in alertas:
            s = sensores.get(a["SensorId"])
            c = comunidades.get(a["ComunidadId"])
            # ON CONFLICT: el esquema nuevo garantiza una sola alerta abierta por fenómeno y
            # comunidad; si la Fase 2 tenía duplicadas, se conserva la primera.
            cur.execute(
                """INSERT INTO alertas (id, comunidad_id, comunidad_nombre, sensor_id, sensor_codigo, sensor_nombre,
                       unidad_medida, nivel, fenomeno, mensaje, valor_disparo, umbral, regla_id, regla_nombre, estado,
                       fecha_hora, fecha_cierre, fecha_modificacion, ciclos_sin_riesgo, reconocida,
                       reconocida_por_usuario_id, reconocida_por, fecha_reconocimiento, cerrada_por_usuario_id, cerrada_por)
                   OVERRIDING SYSTEM VALUE VALUES (%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)
                   ON CONFLICT DO NOTHING""",
                (a["Id"], a["ComunidadId"], c["Nombre"] if c else "", a["SensorId"], s["Codigo"] if s else None,
                 s["Nombre"] if s else None, s["UnidadMedida"] if s else None, a["Nivel"], a["Fenomeno"], a["Mensaje"],
                 a["ValorDisparo"], a["Umbral"], a["ReglaAlertaId"], a["ReglaNombre"] or "Regla integrada", a["Estado"],
                 utc(a["FechaHora"]), utc(a["FechaCierre"]), utc(a["FechaModificacion"]), a["CiclosSinRiesgo"],
                 bool(a["Reconocida"]), a["ReconocidaPorUsuarioId"], nombre_usuario(a["ReconocidaPorUsuarioId"]),
                 utc(a["FechaReconocimiento"]), a["CerradaPorUsuarioId"], nombre_usuario(a["CerradaPorUsuarioId"])))
            omitidas += cur.rowcount == 0
        reiniciar_identidad(cur, "alertas")
        cur.execute("SELECT id FROM alertas")
        ids_alertas = {fila[0] for fila in cur.fetchall()}

        eventos = leer("SELECT * FROM EventosHistorial ORDER BY Id")
        cur.executemany(
            """INSERT INTO eventos_historial (id, comunidad_id, comunidad_nombre, alerta_id, fenomeno, nivel_maximo,
                   descripcion, origen_sensor, sensor_id, valor_registrado, estado, usuario_responsable_id,
                   usuario_responsable, fecha_inicio, fecha_fin, fecha_modificacion)
               OVERRIDING SYSTEM VALUE VALUES (%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)""",
            [(e["Id"], e["ComunidadId"], comunidades.get(e["ComunidadId"], {}).get("Nombre", ""),
              e["AlertaId"] if e["AlertaId"] in ids_alertas else None, e["Fenomeno"], e["NivelMaximo"], e["Descripcion"],
              e["OrigenSensor"] or "", e["SensorId"], e["ValorRegistrado"], e["Estado"], e["UsuarioResponsableId"],
              nombre_usuario(e["UsuarioResponsableId"]), utc(e["FechaInicio"]), utc(e["FechaFin"]), utc(e["FechaModificacion"]))
             for e in eventos])
        reiniciar_identidad(cur, "eventos_historial")

        for tabla, origen_n, clave in (("reglas_alerta", len(reglas), "Reglas"), ("umbrales_sensor", len(sensores), "Umbrales"),
                                       ("alertas", len(alertas), "Alertas"), ("eventos_historial", len(eventos), "Eventos")):
            cur.execute(f"SELECT COUNT(*) FROM {tabla}")
            conteos[clave] = (origen_n, cur.fetchone()[0])
    listo(t, f"{conteos['Reglas'][1]} reglas, {conteos['Umbrales'][1]} umbrales, {conteos['Alertas'][1]} alertas"
             f"{f' ({omitidas} duplicadas abiertas omitidas)' if omitidas else ''}, {conteos['Eventos'][1]} eventos")

    # ------------------------------------------------------------------------------------
    t = paso("Audit Service · bitácora (audit_db)")
    mongo_audit = MongoClient(host="mongo", username="audit_svc", password=env("AUDIT_DB_PASSWORD"), authSource="audit_db", tz_aware=True)
    coleccion = mongo_audit["audit_db"]["eventos"]
    coleccion.delete_many({})
    bitacora = leer("SELECT * FROM Bitacora ORDER BY Id")

    def detalle(texto):
        if not texto:
            return None
        try:
            valor = json.loads(texto)
            return valor if isinstance(valor, dict) else {"valor": valor}
        except ValueError:
            return {"texto": texto}

    ahora = datetime.now(timezone.utc)
    if bitacora:
        coleccion.insert_many([{
            "servicio": "monolito-fase2", "usuarioId": b["UsuarioId"], "usuarioEmail": b["UsuarioEmail"] or "sistema",
            "accion": b["Accion"], "entidad": b["Entidad"], "entidadId": b["EntidadId"], "detalle": detalle(b["Detalle"]),
            "direccionIp": b["DireccionIp"], "correlacion": None, "fechaHora": utc(b["FechaHora"]), "recibidoEn": ahora,
        } for b in bitacora])
    conteos["Bitácora"] = (len(bitacora), coleccion.count_documents({}))
    mongo_audit.close()
    listo(t, f"{conteos['Bitácora'][1]} registros")

    # ------------------------------------------------------------------------------------
    t = paso("Report Service · historial de reportes (report_db)")
    mongo_report = MongoClient(host="mongo", username="report_svc", password=env("REPORT_DB_PASSWORD"), authSource="report_db")
    base = mongo_report["report_db"]
    for nombre in ("reportes", "contadores", "pdf.files", "pdf.chunks"):
        base[nombre].delete_many({})
    mongo_report.close()
    listo(t, "vaciado (la Fase 2 no tenía reportes)")

    origen.close()

    print("\nResumen (origen → destino)")
    for clave, (antes, despues) in conteos.items():
        marca = "✔" if antes == despues else "≠"
        print(f"  {marca} {clave:12} {antes:>9,} → {despues:>9,}")
    if any(a != d for a, d in conteos.values() if True):
        print("\n  (≠ en Alertas solo puede deberse a alertas abiertas duplicadas por fenómeno, que se omiten.)")


if __name__ == "__main__":
    main()

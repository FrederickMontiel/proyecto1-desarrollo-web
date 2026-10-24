import time

from cryptography.hazmat.primitives.asymmetric import rsa

EVENTO = {
    "servicio": "auth-service",
    "usuarioId": 1,
    "usuarioEmail": "admin@swmatrc.local",
    "accion": "InicioSesion",
    "entidad": "Usuario",
    "entidadId": "1",
    "detalle": {"rol": "Administrador"},
    "direccionIp": "10.0.0.5",
    "correlacion": "abc123",
    "fechaHora": "2026-10-22T10:00:00Z",
}


def token_servicio(emitir):
    return emitir(audiencia="swmatrc-internal", permisos=["interno"], rol="Servicio", sub="svc:auth-service")


def cabecera(token):
    return {"authorization": f"Bearer {token}"}


# --- Registro -----------------------------------------------------------------------------


def test_un_servicio_registra_un_evento(cliente, emitir, repositorio):
    respuesta = cliente.post("/internal/events", json=EVENTO, headers=cabecera(token_servicio(emitir)))

    assert respuesta.status_code == 202
    assert respuesta.json() == {"registrados": 1}
    assert repositorio.eventos[0].accion == "InicioSesion"
    assert repositorio.eventos[0].detalle == {"rol": "Administrador"}


def test_un_token_de_usuario_no_puede_registrar_eventos(cliente, emitir):
    token = emitir(permisos=["auditoria:leer", "interno"])  # audiencia web, aunque traiga el permiso
    respuesta = cliente.post("/internal/events", json=EVENTO, headers=cabecera(token))

    assert respuesta.status_code == 401


def test_registrar_sin_token(cliente):
    respuesta = cliente.post("/internal/events", json=EVENTO)

    assert respuesta.status_code == 401
    assert respuesta.headers["content-type"].startswith("application/problem+json")
    assert respuesta.json()["detail"] == "Falta el token de acceso."


def test_rechaza_campos_desconocidos_con_mensaje_en_espanol(cliente, emitir):
    respuesta = cliente.post(
        "/internal/events", json={**EVENTO, "inyectado": True}, headers=cabecera(token_servicio(emitir))
    )

    assert respuesta.status_code == 400
    assert "El campo 'inyectado' no está permitido." in respuesta.json()["detail"]


def test_registro_por_lotes(cliente, emitir, repositorio):
    lote = {"eventos": [{**EVENTO, "accion": f"Accion{i}"} for i in range(3)]}
    respuesta = cliente.post("/internal/events/batch", json=lote, headers=cabecera(token_servicio(emitir)))

    assert respuesta.json() == {"registrados": 3}
    assert len(repositorio.eventos) == 3


# --- Consulta -----------------------------------------------------------------------------


def test_un_administrador_consulta_la_bitacora_paginada(cliente, emitir):
    servicio = cabecera(token_servicio(emitir))
    for i in range(5):
        cliente.post("/internal/events", json={**EVENTO, "fechaHora": f"2026-10-2{i}T10:00:00Z"}, headers=servicio)

    respuesta = cliente.get(
        "/api/audit/events", params={"pagina": 2, "tamano": 2}, headers=cabecera(emitir(permisos=["auditoria:leer"]))
    )

    assert respuesta.status_code == 200
    pagina = respuesta.json()
    assert pagina["totalElementos"] == 5
    assert pagina["totalPaginas"] == 3
    assert [e["fechaHora"][:10] for e in pagina["elementos"]] == ["2026-10-22", "2026-10-21"]


def test_sin_permiso_de_auditoria_no_se_consulta(cliente, emitir):
    respuesta = cliente.get("/api/audit/events", headers=cabecera(emitir(permisos=["alertas:leer"], rol="Operador")))

    assert respuesta.status_code == 403


def test_un_token_de_servicio_no_sirve_para_consultar(cliente, emitir):
    respuesta = cliente.get("/api/audit/events", headers=cabecera(token_servicio(emitir)))

    assert respuesta.status_code == 401


def test_el_tamano_de_pagina_se_acota(cliente, emitir):
    respuesta = cliente.get(
        "/api/audit/events", params={"tamano": 100000}, headers=cabecera(emitir(permisos=["auditoria:leer"]))
    )

    assert respuesta.json()["tamanoPagina"] == 200


def test_rango_de_fechas_invertido(cliente, emitir):
    respuesta = cliente.get(
        "/api/audit/events",
        params={"desde": "2026-10-22", "hasta": "2026-10-01"},
        headers=cabecera(emitir(permisos=["auditoria:leer"])),
    )

    assert respuesta.status_code == 400


def test_catalogos(cliente, emitir):
    servicio = cabecera(token_servicio(emitir))
    cliente.post("/internal/events", json=EVENTO, headers=servicio)
    cliente.post("/internal/events", json={**EVENTO, "accion": "CierreSesion", "servicio": "sensor-service"}, headers=servicio)

    respuesta = cliente.get("/api/audit/catalogs", headers=cabecera(emitir(permisos=["auditoria:leer"])))

    assert respuesta.json() == {
        "acciones": ["CierreSesion", "InicioSesion"],
        "entidades": ["Usuario"],
        "servicios": ["auth-service", "sensor-service"],
    }


# --- Validación del JWT -------------------------------------------------------------------


def test_rechaza_token_vencido(cliente, emitir):
    vencido = emitir(permisos=["auditoria:leer"], exp=int(time.time()) - 3600)

    assert cliente.get("/api/audit/events", headers=cabecera(vencido)).status_code == 401


def test_rechaza_otro_emisor(cliente, emitir):
    ajeno = emitir(permisos=["auditoria:leer"], iss="otro-emisor")

    assert cliente.get("/api/audit/events", headers=cabecera(ajeno)).status_code == 401


def test_rechaza_token_firmado_con_otra_llave(cliente, emitir):
    otra = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    falsificado = emitir(permisos=["auditoria:leer"], llave=otra)

    assert cliente.get("/api/audit/events", headers=cabecera(falsificado)).status_code == 401


def test_el_jwks_se_descarga_una_vez_y_se_reutiliza(cliente, emitir, auth_simulado):
    _, llamadas = auth_simulado
    token = emitir(permisos=["auditoria:leer"])
    for _ in range(5):
        assert cliente.get("/api/audit/events", headers=cabecera(token)).status_code == 200

    assert llamadas["jwks"] == 1


def test_salud(cliente):
    assert cliente.get("/health/live").json() == {"status": "ok"}

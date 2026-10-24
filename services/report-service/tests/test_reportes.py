import asyncio
import importlib.util

import pytest

from app.etiquetas import Formato
from app.pdf import Renderizador
from app.reportes import CATALOGO, Filtros
from conftest import ADMIN, CONSULTA, OPERADOR, DatosFalsos, esperar

# --- Flujo completo por la API ---------------------------------------------------------------


def test_un_operador_solicita_un_reporte_y_se_genera_en_segundo_plano(cliente, token, auditoria):
    respuesta = cliente.post("/api/reports", json={"type": "ALERTS", "filters": {"comunidadId": 1}}, headers=token(OPERADOR, sub="5"))

    assert respuesta.status_code == 202
    creado = respuesta.json()
    assert creado["reportId"].startswith("REP-") and creado["status"] == "PENDING"
    assert creado["fileName"] == f"alertas-{creado['reportId']}.pdf"
    assert creado["generatedBy"] == {"id": 5, "email": "admin@swmatrc.local", "name": "Administrador"}
    assert creado["filters"] == {"comunidadId": 1}

    final = esperar(cliente, creado["reportId"], token(OPERADOR, sub="5"))
    assert final["status"] == "COMPLETED"
    assert final["statusLabel"] == "Completado"
    assert final["rows"] == 1
    assert "fileId" not in final
    assert "ReporteGenerado" in auditoria.acciones


def test_el_id_es_correlativo_por_anio(cliente, token):
    ids = [cliente.post("/api/reports", json={"type": "COMMUNITIES"}, headers=token(ADMIN)).json()["reportId"] for _ in range(3)]

    assert [i[-6:] for i in ids] == ["000001", "000002", "000003"]


def test_rf_rep_10_solo_un_administrador_descarga(cliente, token, auditoria):
    rid = cliente.post("/api/reports", json={"type": "SENSORS"}, headers=token(OPERADOR, sub="5")).json()["reportId"]
    esperar(cliente, rid, token(OPERADOR, sub="5"))

    assert cliente.get(f"/api/reports/{rid}/download", headers=token(OPERADOR, sub="5")).status_code == 403

    descarga = cliente.get(f"/api/reports/{rid}/download", headers=token(ADMIN))
    assert descarga.status_code == 200
    assert descarga.headers["content-type"] == "application/pdf"
    assert f'filename="sensores-{rid}.pdf"' in descarga.headers["content-disposition"]
    assert descarga.content.startswith(b"%PDF")

    meta = cliente.get(f"/api/reports/{rid}", headers=token(ADMIN)).json()
    assert meta["downloadCount"] == 1 and meta["lastDownloadAt"]
    assert "ReporteDescargado" in auditoria.acciones


def test_sin_permiso_de_generar_no_se_solicita(cliente, token):
    assert cliente.post("/api/reports", json={"type": "SENSORS"}, headers=token(CONSULTA)).status_code == 403


def test_sin_token(cliente):
    assert cliente.get("/api/reports").status_code == 401


def test_rf_rep_09_cada_quien_ve_su_historial_y_el_administrador_ve_todo(cliente, token):
    cliente.post("/api/reports", json={"type": "SENSORS"}, headers=token(OPERADOR, sub="5"))
    cliente.post("/api/reports", json={"type": "COMMUNITIES"}, headers=token(OPERADOR, sub="6"))

    propio = cliente.get("/api/reports", headers=token(OPERADOR, sub="5")).json()
    todos = cliente.get("/api/reports", headers=token(ADMIN)).json()

    assert propio["totalElementos"] == 1
    assert todos["totalElementos"] == 2


def test_un_reporte_ajeno_responde_como_inexistente(cliente, token):
    rid = cliente.post("/api/reports", json={"type": "SENSORS"}, headers=token(OPERADOR, sub="5")).json()["reportId"]

    respuesta = cliente.get(f"/api/reports/{rid}", headers=token(OPERADOR, sub="6"))

    assert respuesta.status_code == 404


def test_un_tipo_desconocido_o_un_filtro_no_admitido_se_rechazan(cliente, token):
    desconocido = cliente.post("/api/reports", json={"type": "VOLCANES"}, headers=token(ADMIN))
    filtro = cliente.post("/api/reports", json={"type": "COMMUNITIES", "filters": {"nivel": "Rojo"}}, headers=token(ADMIN))
    valor = cliente.post("/api/reports", json={"type": "ALERTS", "filters": {"nivel": "Morado"}}, headers=token(ADMIN))
    rango = cliente.post("/api/reports", json={"type": "ALERTS", "filters": {"desde": "2026-10-22T00:00:00Z",
                                                                              "hasta": "2026-10-01T00:00:00Z"}}, headers=token(ADMIN))

    assert desconocido.status_code == 400 and "VOLCANES" in desconocido.json()["detail"]
    assert filtro.status_code == 400 and "no admite los filtros: nivel" in filtro.json()["detail"]
    assert valor.status_code == 400 and "debe ser uno de" in valor.json()["detail"]
    assert rango.status_code == 400 and "posterior" in rango.json()["detail"]


def test_si_un_servicio_cae_el_reporte_queda_fallido_con_un_mensaje_claro(cliente, token, datos):
    datos.caidos.add("Alert Service")
    rid = cliente.post("/api/reports", json={"type": "ALERTS"}, headers=token(ADMIN)).json()["reportId"]

    final = esperar(cliente, rid, token(ADMIN))

    assert final["status"] == "FAILED"
    assert "Alert Service no respondió" in final["error"]
    assert cliente.get(f"/api/reports/{rid}/download", headers=token(ADMIN)).status_code == 409


def test_catalogo_de_tipos(cliente, token):
    tipos = cliente.get("/api/reports/types", headers=token(OPERADOR)).json()

    assert [t["requirement"] for t in tipos] == [f"RF-REP-0{i}" for i in range(1, 9)]


def test_dashboard_sigue_aunque_un_servicio_no_responda(cliente, token, datos):
    datos.caidos.add("Alert Service")

    d = cliente.get("/api/reports/dashboard", headers=token(CONSULTA)).json()

    assert d["sensores"]["sinComunicacion"] == 1
    assert d["comunidades"] == {"total": 2, "activas": 1}
    assert d["alertas"] is None
    assert d["disponibles"] == {"community": True, "sensor": True, "alert": False}


# --- Contenido de cada reporte ---------------------------------------------------------------


@pytest.mark.parametrize("codigo", list(CATALOGO))
def test_cada_reporte_arma_su_documento_y_su_html(codigo):
    formato = Formato("America/Guatemala")
    tipo = CATALOGO[codigo]

    documento = asyncio.run(tipo.generador(DatosFalsos(), Filtros(), formato, 10_000))
    html = Renderizador(formato).html(tipo, documento, {"reportId": "REP-2026-000042", "usuario": "Ana",
                                                        "generatedAt": "2026-10-22T18:30:00Z"}, Filtros())

    assert documento.secciones
    assert "REP-2026-000042" in html and tipo.nombre in html
    assert 'counter(page) " de " counter(pages)' in html        # numeración (RNF-REP-03)
    assert "Logo SWMATRC" in html
    assert "22/10/2026 12:30" in html                           # hora de Guatemala (UTC-6)


def test_el_reporte_de_alertas_trae_las_columnas_del_requisito_y_escapa_el_html():
    formato = Formato("America/Guatemala")
    documento = asyncio.run(CATALOGO["ALERTS"].generador(DatosFalsos(), Filtros(), formato, 10_000))
    tabla = next(s for s in documento.secciones if s["tipo"] == "tabla")
    html = Renderizador(formato).html(CATALOGO["ALERTS"], documento,
                                      {"reportId": "R", "usuario": "Ana", "generatedAt": "2026-10-22T18:30:00Z"}, Filtros())

    assert tabla["columnas"] == ["Fecha", "Hora", "Comunidad", "Sensor", "Fenómeno", "Nivel", "Valor", "Umbral", "Mensaje", "Estado"]
    assert tabla["filas"][0][4] == "Inundación"
    assert "&lt;b&gt;¡ya!&lt;/b&gt;" in html and "<b>¡ya!</b>" not in html
    assert 'class="nivel Rojo"' in html


def test_las_lecturas_avisan_cuando_se_recortan():
    datos = DatosFalsos()
    datos.total_lecturas = 50

    documento = asyncio.run(CATALOGO["READINGS"].generador(datos, Filtros(), Formato("America/Guatemala"), 10))

    assert documento.filas == 10
    assert "10 lecturas más recientes de un total de 50" in documento.avisos[0]


def test_las_estadisticas_informan_la_lluvia_acumulada():
    documento = asyncio.run(CATALOGO["CLIMATE_STATS"].generador(DatosFalsos(), Filtros(), Formato("America/Guatemala"), 10))
    tabla = next(s for s in documento.secciones if s["tipo"] == "tabla")

    lluvia = next(f for f in tabla["filas"] if f[0] == "Lluvia")
    assert lluvia[5] == "250 mm"


def test_los_filtros_se_describen_con_nombres_legibles():
    formato = Formato("America/Guatemala")
    filtros = Filtros(comunidadId=1, fenomeno="IncendioForestal", activa=True)

    descritos = Renderizador(formato).describir_filtros(filtros, {"comunidadId": "Aldea El Rosario"})

    assert descritos == [("Comunidad", "Aldea El Rosario"), ("Fenómeno", "Incendio forestal"), ("Activa", "Sí")]


# --- PDF real (solo donde están las librerías nativas de WeasyPrint: la imagen Docker) --------


def _weasyprint_disponible() -> bool:
    if importlib.util.find_spec("weasyprint") is None:
        return False
    try:
        import weasyprint  # noqa: F401
    except OSError:
        return False
    return True


@pytest.mark.skipif(not _weasyprint_disponible(), reason="WeasyPrint necesita Pango; se prueba dentro de Docker.")
def test_genera_un_pdf_real_de_varias_paginas():
    formato = Formato("America/Guatemala")
    datos = DatosFalsos()
    datos.total_lecturas = 400
    tipo = CATALOGO["READINGS"]
    documento = asyncio.run(tipo.generador(datos, Filtros(), formato, 10_000))
    renderizador = Renderizador(formato)
    html = renderizador.html(tipo, documento, {"reportId": "REP-2026-000001", "usuario": "Ana",
                                               "generatedAt": "2026-10-22T18:30:00Z"}, Filtros())

    pdf = asyncio.run(renderizador.pdf(html))

    assert pdf.startswith(b"%PDF")
    assert pdf.count(b"/Type /Page\n") + pdf.count(b"/Type /Page ") + pdf.count(b"/Type/Page") >= 2 or len(pdf) > 20_000

"""
Catálogo de reportes (RF-REP-01 a RF-REP-08) y sus generadores.

Cada generador obtiene los datos de las API de los demás servicios y arma un *documento*:
una lista de secciones (indicadores, tablas, barras) que la plantilla Jinja2 convierte en
HTML y WeasyPrint en PDF. Los generadores no saben nada de HTML.
"""

from collections import Counter
from dataclasses import dataclass, field
from datetime import datetime
from typing import Any, Awaitable, Callable

from pydantic import BaseModel, ConfigDict, Field, field_validator

from .clientes import ClienteServicios
from .etiquetas import ORDEN_NIVELES, Formato, etiqueta

TIPOS_SENSOR = ["Temperatura", "Humedad", "Viento", "Lluvia", "NivelRio", "NivelReservorio", "Humo", "Otro"]
ESTADOS_SENSOR = ["Activo", "Inactivo", "SinSenal"]
FENOMENOS = ["Inundacion", "Sequia", "Tormenta", "Helada", "IncendioForestal"]
NIVELES = ["Verde", "Amarillo", "Naranja", "Rojo"]
ESTADOS_ALERTA = ["Activa", "Atendida", "Cerrada"]


class Filtros(BaseModel):
    """Filtros que puede recibir un reporte. Cada tipo admite solo un subconjunto."""

    model_config = ConfigDict(extra="forbid")

    comunidadId: int | None = Field(default=None, ge=1)
    sensorId: int | None = Field(default=None, ge=1)
    tipoSensor: str | None = None
    estadoSensor: str | None = None
    fenomeno: str | None = None
    nivel: str | None = None
    estado: str | None = None
    activa: bool | None = None
    departamento: str | None = Field(default=None, max_length=120)
    desde: datetime | None = None
    hasta: datetime | None = None

    @field_validator("tipoSensor")
    @classmethod
    def _tipo(cls, v):
        return _en(v, TIPOS_SENSOR, "tipoSensor")

    @field_validator("estadoSensor")
    @classmethod
    def _estado_sensor(cls, v):
        return _en(v, ESTADOS_SENSOR, "estadoSensor")

    @field_validator("fenomeno")
    @classmethod
    def _fenomeno(cls, v):
        return _en(v, FENOMENOS, "fenomeno")

    @field_validator("nivel")
    @classmethod
    def _nivel(cls, v):
        return _en(v, NIVELES, "nivel")

    @field_validator("estado")
    @classmethod
    def _estado(cls, v):
        return _en(v, ESTADOS_ALERTA, "estado")

    def usados(self) -> dict[str, Any]:
        return self.model_dump(exclude_none=True)


def _en(valor, permitidos, campo):
    if valor is not None and valor not in permitidos:
        raise ValueError(f"El filtro '{campo}' debe ser uno de: {', '.join(permitidos)}.")
    return valor


# ---------------------------------------------------------------------------------------
# Documento intermedio
# ---------------------------------------------------------------------------------------


@dataclass
class Documento:
    secciones: list[dict] = field(default_factory=list)
    filas: int = 0
    avisos: list[str] = field(default_factory=list)

    def indicadores(self, titulo: str, items: list[tuple[str, str]]) -> None:
        self.secciones.append({"tipo": "indicadores", "titulo": titulo, "elementos": items})

    def tabla(self, titulo: str, columnas: list[str], filas: list[list[Any]], nota: str | None = None,
              anchos: list[str] | None = None, nivel_columna: int | None = None) -> None:
        self.filas += len(filas)
        self.secciones.append({"tipo": "tabla", "titulo": titulo, "columnas": columnas, "filas": filas,
                               "nota": nota, "anchos": anchos, "nivel_columna": nivel_columna})

    def barras(self, titulo: str, datos: dict[str, int], orden: list[str] | None = None) -> None:
        claves = [k for k in (orden or []) if k in datos] + [k for k in datos if k not in (orden or [])]
        maximo = max(datos.values(), default=0) or 1
        self.secciones.append({"tipo": "barras", "titulo": titulo,
                               "elementos": [(etiqueta(k), datos[k], round(100 * datos[k] / maximo), k) for k in claves]})

    def recorte(self, mostradas: int, total: int, que: str) -> None:
        if total > mostradas:
            self.avisos.append(
                f"Se muestran las {mostradas:,} {que} más recientes de un total de {total:,}. "
                "Acote los filtros (por ejemplo, el rango de fechas) para ver el resto.")


Generador = Callable[[ClienteServicios, Filtros, Formato, int], Awaitable[Documento]]


@dataclass(frozen=True)
class TipoReporte:
    codigo: str
    requisito: str
    nombre: str
    descripcion: str
    filtros: tuple[str, ...]
    archivo: str
    generador: Generador
    horizontal: bool = False


# ---------------------------------------------------------------------------------------
# RF-REP-01 — Comunidades
# ---------------------------------------------------------------------------------------


async def _comunidades(c: ClienteServicios, f: Filtros, fmt: Formato, limite: int) -> Documento:
    comunidades = await c.comunidades(activa=f.activa)
    conteo = await c.conteo_sensores()
    if f.departamento:
        comunidades = [x for x in comunidades if x["departamento"].lower() == f.departamento.lower()]

    doc = Documento()
    doc.indicadores("Resumen", [
        ("Comunidades", fmt.entero(len(comunidades))),
        ("Activas", fmt.entero(sum(1 for x in comunidades if x["activa"]))),
        ("Sensores instalados", fmt.entero(sum(conteo.get(x["id"], {}).get("total", 0) for x in comunidades))),
        ("Población cubierta", fmt.entero(sum(x.get("poblacion") or 0 for x in comunidades))),
    ])
    doc.tabla("Comunidades monitoreadas",
              ["Nombre", "Municipio", "Departamento", "País", "Coordenadas", "Estado", "Sensores"],
              [[x["nombre"], x["municipio"], x["departamento"], x["pais"], fmt.coordenadas(x["latitud"], x["longitud"]),
                "Activa" if x["activa"] else "Inactiva", fmt.entero(conteo.get(x["id"], {}).get("total", 0))]
               for x in sorted(comunidades, key=lambda x: x["nombre"])])
    return doc


# ---------------------------------------------------------------------------------------
# RF-REP-02 — Sensores
# ---------------------------------------------------------------------------------------


async def _sensores(c: ClienteServicios, f: Filtros, fmt: Formato, limite: int) -> Documento:
    sensores = await c.sensores(comunidadId=f.comunidadId, tipo=f.tipoSensor, estado=f.estadoSensor)
    doc = Documento()
    doc.barras("Sensores por tipo", dict(Counter(s["tipo"] for s in sensores)))
    doc.tabla("Inventario de sensores",
              ["Código", "Nombre", "Tipo", "Comunidad", "Ubicación", "Unidad", "Estado", "Instalación"],
              [[s["codigo"], s["nombre"], etiqueta(s["tipo"]), s["comunidadNombre"] or f"#{s['comunidadId']}",
                s.get("ubicacion") or "—", s["unidadMedida"], etiqueta(s["estado"]), fmt.fecha(s.get("fechaInstalacion"))]
               for s in sensores])
    return doc


# ---------------------------------------------------------------------------------------
# RF-REP-03 — Lecturas
# ---------------------------------------------------------------------------------------


async def _lecturas(c: ClienteServicios, f: Filtros, fmt: Formato, limite: int) -> Documento:
    filas, total = await c.lecturas(limite, sensorId=f.sensorId, comunidadId=f.comunidadId, tipo=f.tipoSensor,
                                    desde=f.desde, hasta=f.hasta)
    doc = Documento()
    doc.indicadores("Resumen", [
        ("Lecturas en el periodo", fmt.entero(total)),
        ("Sensores distintos", fmt.entero(len({x["sensorId"] for x in filas}))),
        ("Comunidades", fmt.entero(len({x["comunidadId"] for x in filas}))),
    ])
    doc.recorte(len(filas), total, "lecturas")
    doc.tabla("Lecturas registradas",
              ["Sensor", "Comunidad", "Fecha", "Hora", "Valor", "Unidad", "Estado", "Nivel"],
              [[f"{x['sensorCodigo']} — {x['sensorNombre']}", x["comunidadNombre"], fmt.fecha(x["fechaHora"]),
                fmt.hora(x["fechaHora"]), fmt.numero(x["valor"]), x["unidadMedida"], etiqueta(x["estadoSensor"]),
                etiqueta(x.get("nivel"))] for x in filas],
              nivel_columna=7)
    return doc


# ---------------------------------------------------------------------------------------
# RF-REP-04 — Alertas
# ---------------------------------------------------------------------------------------


async def _alertas(c: ClienteServicios, f: Filtros, fmt: Formato, limite: int) -> Documento:
    filas, total = await c.alertas(limite, comunidadId=f.comunidadId, sensorId=f.sensorId, fenomeno=f.fenomeno,
                                   nivel=f.nivel, estado=f.estado, desde=f.desde, hasta=f.hasta)
    doc = Documento()
    doc.barras("Alertas por nivel", dict(Counter(a["nivel"] for a in filas)), ORDEN_NIVELES)
    doc.barras("Alertas por fenómeno", dict(Counter(a["fenomeno"] for a in filas)))
    doc.recorte(len(filas), total, "alertas")
    doc.tabla("Alertas emitidas",
              ["Fecha", "Hora", "Comunidad", "Sensor", "Fenómeno", "Nivel", "Valor", "Umbral", "Mensaje", "Estado"],
              [[fmt.fecha(a["fechaHora"]), fmt.hora(a["fechaHora"]), a["comunidadNombre"], a.get("sensorCodigo") or "Combinada",
                etiqueta(a["fenomeno"]), etiqueta(a["nivel"]), fmt.numero(a.get("valorDisparo")),
                fmt.numero(a.get("umbral")), a["mensaje"], etiqueta(a["estado"])] for a in filas],
              anchos=["7%", "6%", "11%", "8%", "9%", "7%", "6%", "6%", "32%", "8%"], nivel_columna=5)
    return doc


# ---------------------------------------------------------------------------------------
# RF-REP-05 — Historial de eventos
# ---------------------------------------------------------------------------------------


async def _eventos(c: ClienteServicios, f: Filtros, fmt: Formato, limite: int) -> Documento:
    parametros = dict(comunidadId=f.comunidadId, fenomeno=f.fenomeno, nivel=f.nivel, estado=f.estado,
                      desde=f.desde, hasta=f.hasta)
    estadisticas = await c.estadisticas_eventos(**parametros)
    filas, total = await c.eventos(limite, **parametros)

    doc = Documento()
    promedio = estadisticas.get("duracionPromedioMinutos")
    doc.indicadores("Resumen", [
        ("Eventos", fmt.entero(estadisticas["totalEventos"])),
        ("En curso", fmt.entero(estadisticas["eventosAbiertos"])),
        ("Duración promedio", f"{fmt.numero(promedio, 1)} min" if promedio is not None else "—"),
    ])
    doc.barras("Eventos por fenómeno", estadisticas["porFenomeno"])
    doc.barras("Eventos por nivel máximo", estadisticas["porNivel"], ORDEN_NIVELES)
    doc.recorte(len(filas), total, "eventos")
    doc.tabla("Registro de fenómenos",
              ["Inicio", "Fin", "Comunidad", "Fenómeno", "Nivel máx.", "Origen", "Valor", "Estado", "Responsable", "Duración"],
              [[fmt.fecha_hora(e["fechaInicio"]), fmt.fecha_hora(e.get("fechaFin")), e["comunidadNombre"],
                etiqueta(e["fenomeno"]), etiqueta(e["nivelMaximo"]), e["origenSensor"], fmt.numero(e.get("valorRegistrado")),
                etiqueta(e["estado"]), e.get("usuarioResponsable") or "Sistema",
                f"{fmt.numero(e['duracionMinutos'], 1)} min" if e.get("duracionMinutos") is not None else "En curso"]
               for e in filas],
              nivel_columna=4)
    return doc


# ---------------------------------------------------------------------------------------
# RF-REP-06 — Estado de sensores
# ---------------------------------------------------------------------------------------


async def _estado_sensores(c: ClienteServicios, f: Filtros, fmt: Formato, limite: int) -> Documento:
    sensores = await c.sensores(comunidadId=f.comunidadId)
    por_estado = Counter(s["estado"] for s in sensores)

    doc = Documento()
    doc.indicadores("Estado de la red", [
        ("Total", fmt.entero(len(sensores))),
        ("Activos", fmt.entero(por_estado.get("Activo", 0))),
        ("Inactivos", fmt.entero(por_estado.get("Inactivo", 0))),
        ("Sin comunicación", fmt.entero(por_estado.get("SinSenal", 0))),
    ])
    # Primero los que requieren atención: sin comunicación, luego inactivos.
    for estado, titulo in (("SinSenal", "Sensores sin comunicación"), ("Inactivo", "Sensores inactivos"), ("Activo", "Sensores activos")):
        grupo = [s for s in sensores if s["estado"] == estado]
        if not grupo:
            continue
        doc.tabla(titulo, ["Código", "Nombre", "Tipo", "Comunidad", "Última lectura", "Último valor", "Nivel actual"],
                  [[s["codigo"], s["nombre"], etiqueta(s["tipo"]), s["comunidadNombre"], fmt.fecha_hora(s.get("ultimaLectura")),
                    f"{fmt.numero(s['valorActual'])} {s['unidadMedida']}", etiqueta(s.get("nivelActual"))] for s in grupo],
                  nivel_columna=6)
    return doc


# ---------------------------------------------------------------------------------------
# RF-REP-07 — Estadísticas climáticas
# ---------------------------------------------------------------------------------------

_MAGNITUDES = [
    ("Temperatura", "Temperatura"), ("Humedad", "Humedad relativa"), ("Viento", "Velocidad del viento"),
    ("Lluvia", "Lluvia"), ("NivelRio", "Nivel de río"), ("NivelReservorio", "Nivel de reservorio"), ("Humo", "Partículas (humo)"),
]


async def _estadisticas(c: ClienteServicios, f: Filtros, fmt: Formato, limite: int) -> Documento:
    rango = dict(comunidadId=f.comunidadId, desde=f.desde, hasta=f.hasta)
    estadisticas = {e["tipo"]: e for e in await c.estadisticas_lecturas(**rango)}
    resumen = await c.resumen_alertas(**rango)

    doc = Documento()
    filas = []
    for tipo, nombre in _MAGNITUDES:
        e = estadisticas.get(tipo)
        if not e:
            continue
        u = e["unidadMedida"]
        filas.append([nombre, fmt.entero(e["lecturas"]), f"{fmt.numero(e['promedio'])} {u}",
                      f"{fmt.numero(e['minimo'])} {u}", f"{fmt.numero(e['maximo'])} {u}",
                      # La lluvia se informa también acumulada: es la cifra que interesa a protección civil.
                      f"{fmt.numero(e['suma'])} {u}" if tipo == "Lluvia" else "—"])
    if not filas:
        doc.avisos.append("No hay lecturas en el periodo seleccionado.")
    doc.tabla("Estadísticas por magnitud", ["Magnitud", "Lecturas", "Promedio", "Mínimo", "Máximo", "Acumulado"], filas)
    doc.indicadores("Alertas en el periodo", [
        ("Alertas emitidas", fmt.entero(resumen["alertasEnPeriodo"])),
        ("Activas ahora", fmt.entero(resumen["alertasActivas"])),
        ("Nivel actual", etiqueta(resumen["nivelGlobal"])),
    ])
    doc.barras("Alertas del periodo por nivel", resumen["alertasPeriodoPorNivel"], ORDEN_NIVELES)
    doc.barras("Alertas del periodo por fenómeno", resumen["alertasPeriodoPorFenomeno"])
    return doc


# ---------------------------------------------------------------------------------------
# RF-REP-08 — Ejecutivo
# ---------------------------------------------------------------------------------------


async def _ejecutivo(c: ClienteServicios, f: Filtros, fmt: Formato, limite: int) -> Documento:
    rango = dict(comunidadId=f.comunidadId, desde=f.desde, hasta=f.hasta)
    comunidades = await c.comunidades()
    sensores = await c.sensores(comunidadId=f.comunidadId)
    resumen = await c.resumen_alertas(**rango)
    eventos = await c.estadisticas_eventos(**rango)
    activas = (await c.alertas_activas(f.comunidadId))["alertas"]
    if f.comunidadId:
        comunidades = [x for x in comunidades if x["id"] == f.comunidadId]

    por_estado = Counter(s["estado"] for s in sensores)
    operativos = por_estado.get("Activo", 0)
    disponibilidad = f"{100 * operativos / len(sensores):.0f} %" if sensores else "—"

    doc = Documento()
    doc.indicadores("Situación climática", [
        ("Nivel de alerta actual", etiqueta(resumen["nivelGlobal"])),
        ("Alertas activas", fmt.entero(resumen["alertasActivas"])),
        ("Alertas en el periodo", fmt.entero(resumen["alertasEnPeriodo"])),
        ("Eventos registrados", fmt.entero(eventos["totalEventos"])),
    ])
    doc.indicadores("Red de monitoreo", [
        ("Comunidades activas", f"{sum(1 for x in comunidades if x['activa'])} de {len(comunidades)}"),
        ("Sensores", fmt.entero(len(sensores))),
        ("Disponibilidad de sensores", disponibilidad),
        ("Sin comunicación", fmt.entero(por_estado.get("SinSenal", 0))),
    ])
    doc.barras("Alertas del periodo por nivel", resumen["alertasPeriodoPorNivel"], ORDEN_NIVELES)
    doc.barras("Eventos por fenómeno", eventos["porFenomeno"])
    doc.barras("Eventos por comunidad", eventos["porComunidad"])
    doc.tabla("Alertas activas en este momento", ["Comunidad", "Fenómeno", "Nivel", "Desde", "Mensaje"],
              [[a["comunidadNombre"], etiqueta(a["fenomeno"]), etiqueta(a["nivel"]), fmt.fecha_hora(a["fechaHora"]), a["mensaje"]]
               for a in activas],
              nota=None if activas else "No hay alertas activas.", anchos=["16%", "13%", "9%", "13%", "49%"], nivel_columna=2)
    return doc


# ---------------------------------------------------------------------------------------

CATALOGO: dict[str, TipoReporte] = {t.codigo: t for t in [
    TipoReporte("COMMUNITIES", "RF-REP-01", "Reporte de comunidades",
                "Comunidades monitoreadas con su ubicación, estado y cantidad de sensores.",
                ("activa", "departamento"), "comunidades", _comunidades),
    TipoReporte("SENSORS", "RF-REP-02", "Reporte de sensores",
                "Inventario de sensores: tipo, comunidad, ubicación, unidad, estado e instalación.",
                ("comunidadId", "tipoSensor", "estadoSensor"), "sensores", _sensores, horizontal=True),
    TipoReporte("READINGS", "RF-REP-03", "Reporte de lecturas",
                "Lecturas por sensor, comunidad, tipo y rango de fechas.",
                ("sensorId", "comunidadId", "tipoSensor", "desde", "hasta"), "lecturas", _lecturas, horizontal=True),
    TipoReporte("ALERTS", "RF-REP-04", "Reporte de alertas",
                "Alertas emitidas con su fenómeno, nivel, valor, umbral, mensaje y estado.",
                ("comunidadId", "sensorId", "fenomeno", "nivel", "estado", "desde", "hasta"), "alertas", _alertas, horizontal=True),
    TipoReporte("EVENT_HISTORY", "RF-REP-05", "Historial de eventos",
                "Registro de fenómenos: inundación, sequía, tormenta, helada e incendio forestal.",
                ("comunidadId", "fenomeno", "nivel", "estado", "desde", "hasta"), "historial-eventos", _eventos, horizontal=True),
    TipoReporte("SENSOR_STATUS", "RF-REP-06", "Estado de sensores",
                "Sensores activos, inactivos y sin comunicación, con su última lectura.",
                ("comunidadId",), "estado-sensores", _estado_sensores, horizontal=True),
    TipoReporte("CLIMATE_STATS", "RF-REP-07", "Estadísticas climáticas",
                "Promedio, mínimo y máximo por magnitud, lluvia acumulada y alertas del periodo.",
                ("comunidadId", "desde", "hasta"), "estadisticas-climaticas", _estadisticas),
    TipoReporte("EXECUTIVE", "RF-REP-08", "Reporte ejecutivo",
                "Situación climática global: comunidades, sensores, alertas, eventos e indicadores.",
                ("comunidadId", "desde", "hasta"), "reporte-ejecutivo", _ejecutivo),
]}

import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, forkJoin, map, of, switchMap } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  ActualizarSensor,
  ActualizarUsuario,
  Alerta,
  Comunidad,
  CrearSensor,
  EstadisticasHistorial,
  EstadoAlerta,
  EstadoComunidad,
  Evento,
  EstadoSensor,
  GuardarComunidad,
  GuardarRegla,
  IndicadoresDashboard,
  LecturaHistorica,
  NivelAlerta,
  ORDEN_TIPOS_SENSOR,
  Pagina,
  PlantillaSensor,
  ReglaAlerta,
  RegistroUsuario,
  RegistroBitacora,
  Reporte,
  ResumenDashboard,
  RolUsuario,
  SerieHistorica,
  Sensor,
  SolicitudReporte,
  TipoFenomeno,
  TipoReporte,
  TipoSensor,
  Umbrales,
  Usuario,
} from '../modelos/modelos';

/** Paginación común a los listados largos. */
interface Paginado {
  pagina?: number;
  tamano?: number;
}

/** Rango de fechas en ISO 8601. */
interface RangoFechas {
  desde?: string;
  hasta?: string;
}

/** Umbrales tal como los devuelve Alert Service (`/api/alerts/thresholds`). */
interface UmbralesAlert {
  sensorId: number;
  tipoSensor: TipoSensor;
  umbralAmarilloAlto: number | null;
  umbralNaranjaAlto: number | null;
  umbralRojoAlto: number | null;
  umbralAmarilloBajo: number | null;
  umbralNaranjaBajo: number | null;
  umbralRojoBajo: number | null;
}

/** Plantilla de umbrales por tipo (`/api/alerts/thresholds/templates`). */
type PlantillasUmbrales = Record<
  TipoSensor,
  {
    amarilloAlto: number | null;
    naranjaAlto: number | null;
    rojoAlto: number | null;
    amarilloBajo: number | null;
    naranjaBajo: number | null;
    rojoBajo: number | null;
  }
>;

const SIN_UMBRALES: Umbrales = {
  umbralAmarilloAlto: null,
  umbralNaranjaAlto: null,
  umbralRojoAlto: null,
  umbralAmarilloBajo: null,
  umbralNaranjaBajo: null,
  umbralRojoBajo: null,
};

/**
 * Cliente HTTP de la API. Un único punto donde vive la forma de cada extremo.
 *
 * En la Fase 3 el backend se dividió en microservicios detrás del API Gateway
 * (`/api/auth`, `/api/communities`, `/api/sensors`, `/api/alerts`, `/api/reports`,
 * `/api/audit`). Este servicio conserva las mismas firmas de la Fase 2 y traduce por dentro:
 * las páginas no necesitan saber qué microservicio responde cada dato. El caso más visible
 * son los umbrales, que ahora pertenecen al Alert Service y aquí se combinan con el sensor.
 */
@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);
  private readonly base = environment.urlApi;

  // --- Monitoreo ---

  comunidades(): Observable<Comunidad[]> {
    return this.http.get<Comunidad[]>(`${this.base}/communities`);
  }

  estadoComunidad(comunidadId: number): Observable<EstadoComunidad> {
    return this.http
      .get<EstadoComunidad>(`${this.base}/sensors/communities/${comunidadId}/state`)
      .pipe(map((e) => ({ ...e, sensores: e.sensores.map((s) => this.normalizarSensor(s)) })));
  }

  series(comunidadId: number, minutos = 30): Observable<SerieHistorica[]> {
    return this.http.get<SerieHistorica[]>(`${this.base}/sensors/communities/${comunidadId}/series`, {
      params: new HttpParams().set('minutos', minutos),
    });
  }

  /**
   * Indicadores del tablero. Report Service los agrega de Community, Sensor y Alert en una
   * sola llamada (sección 15 del documento).
   */
  resumen(comunidadId?: number): Observable<ResumenDashboard> {
    return this.indicadores(comunidadId).pipe(
      map((d) => ({
        totalComunidades: d.comunidades?.total ?? 0,
        comunidadesActivas: d.comunidades?.activas ?? 0,
        totalSensores: d.sensores?.total ?? 0,
        sensoresActivos: d.sensores?.activos ?? 0,
        sensoresInactivos: d.sensores?.inactivos ?? 0,
        sensoresSinSenal: d.sensores?.sinComunicacion ?? 0,
        sensoresPorTipo: d.sensores?.porTipo ?? {},
        alertasActivas: d.alertas?.alertasActivas ?? 0,
        eventosUltimas24h: d.alertas?.eventosUltimas24h ?? 0,
        nivelGlobal: d.alertas?.nivelGlobal ?? 'Verde',
        alertasPorNivel: d.alertas?.alertasPorNivel ?? {},
        alertasPorFenomeno: d.alertas?.alertasPorFenomeno ?? {},
        eventosPorFenomeno: d.alertas?.eventosPorFenomeno ?? {},
        alertasPeriodoPorNivel: d.alertas?.alertasPeriodoPorNivel ?? {},
        alertasPeriodoPorFenomeno: d.alertas?.alertasPeriodoPorFenomeno ?? {},
        alertasEnPeriodo: d.alertas?.alertasEnPeriodo ?? 0,
        totalEventosPeriodo: d.eventos?.totalEventos ?? 0,
        estadisticasClimaticas: d.estadisticasClimaticas ?? [],
        disponibles: d.disponibles,
      })),
    );
  }

  indicadores(comunidadId?: number, desde?: string, hasta?: string): Observable<IndicadoresDashboard> {
    return this.http.get<IndicadoresDashboard>(`${this.base}/reports/dashboard`, {
      params: this.aParams({ comunidadId, desde, hasta }),
    });
  }

  // --- Comunidades ---

  listarComunidades(
    filtro: { busqueda?: string; activa?: boolean; municipio?: string; departamento?: string } = {},
  ): Observable<Comunidad[]> {
    return this.http.get<Comunidad[]>(`${this.base}/communities`, { params: this.aParams(filtro) });
  }

  crearComunidad(datos: GuardarComunidad): Observable<Comunidad> {
    return this.http.post<Comunidad>(`${this.base}/communities`, datos);
  }

  actualizarComunidad(id: number, datos: GuardarComunidad): Observable<Comunidad> {
    return this.http.put<Comunidad>(`${this.base}/communities/${id}`, datos);
  }

  cambiarEstadoComunidad(id: number, activa: boolean): Observable<Comunidad> {
    return this.http.patch<Comunidad>(`${this.base}/communities/${id}/status`, { activa });
  }

  // --- Sensores ---

  /**
   * Sensores con sus umbrales. Sensor Service guarda la parte física; Alert Service, los
   * umbrales: los recalibrados se piden aparte y los demás salen de la plantilla del tipo.
   */
  sensores(
    filtro: { comunidadId?: number; tipo?: TipoSensor; estado?: EstadoSensor; codigo?: string } = {},
  ): Observable<Sensor[]> {
    return forkJoin({
      sensores: this.http.get<Sensor[]>(`${this.base}/sensors`, { params: this.aParams(filtro) }),
      calibrados: this.http.get<UmbralesAlert[]>(`${this.base}/alerts/thresholds`).pipe(catchError(() => of([]))),
      plantillas: this.plantillasUmbrales(),
    }).pipe(
      map(({ sensores, calibrados, plantillas }) => {
        const porSensor = new Map(calibrados.map((u) => [u.sensorId, u]));
        return sensores.map((s) =>
          this.normalizarSensor({ ...s, ...this.umbralesDe(porSensor.get(s.id), plantillas, s.tipo) }),
        );
      }),
    );
  }

  /** Calibración de fábrica: la física de Sensor Service más los umbrales recomendados de Alert Service. */
  plantillasSensor(): Observable<PlantillaSensor[]> {
    return forkJoin({
      fisicas: this.http.get<Omit<PlantillaSensor, keyof Umbrales>[]>(`${this.base}/sensors/templates`),
      umbrales: this.plantillasUmbrales(),
    }).pipe(map(({ fisicas, umbrales }) => fisicas.map((p) => ({ ...p, ...this.umbralesDe(undefined, umbrales, p.tipo) }))));
  }

  crearSensor(sensor: CrearSensor): Observable<Sensor> {
    const { umbrales, resto } = this.separarUmbrales(sensor);
    const tipo = typeof sensor.tipo === 'number' ? ORDEN_TIPOS_SENSOR[sensor.tipo - 1] : sensor.tipo;

    return this.http.post<Sensor>(`${this.base}/sensors`, { ...resto, tipo }).pipe(
      switchMap((creado) => this.guardarUmbrales(creado, umbrales)),
    );
  }

  actualizarSensor(id: number, cambios: ActualizarSensor): Observable<Sensor> {
    const { umbrales, resto } = this.separarUmbrales(cambios);
    return this.http
      .put<Sensor>(`${this.base}/sensors/${id}`, resto)
      .pipe(switchMap((actualizado) => this.guardarUmbrales(actualizado, umbrales)));
  }

  cambiarEstadoSensor(id: number, estado: EstadoSensor): Observable<Sensor> {
    return this.http.patch<Sensor>(`${this.base}/sensors/${id}/status`, { estado }).pipe(map((s) => this.normalizarSensor(s)));
  }

  establecerValorSensor(id: number, valor: number): Observable<LecturaHistorica> {
    return this.http.post<LecturaHistorica>(`${this.base}/sensors/${id}/value`, { valor });
  }

  /** Borra el sensor y, si tenía, su calibración en Alert Service. */
  eliminarSensor(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/sensors/${id}`).pipe(
      switchMap(() => this.http.delete<void>(`${this.base}/alerts/thresholds/${id}`).pipe(catchError(() => of(undefined)))),
    );
  }

  // --- Lecturas ---

  lecturas(
    filtro: Paginado & RangoFechas & { sensorId?: number; comunidadId?: number; tipo?: TipoSensor },
  ): Observable<Pagina<LecturaHistorica>> {
    return this.http.get<Pagina<LecturaHistorica>>(`${this.base}/sensors/readings`, {
      params: this.aParams(filtro),
    });
  }

  registrarLectura(sensorId: number, valor: number): Observable<LecturaHistorica> {
    return this.http.post<LecturaHistorica>(`${this.base}/sensors/readings`, { sensorId, valor });
  }

  eliminarLectura(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/sensors/readings/${id}`);
  }

  // --- Reglas de alerta ---

  reglas(
    filtro: { busqueda?: string; tipoSensor?: TipoSensor; fenomeno?: TipoFenomeno; nivel?: NivelAlerta; activa?: boolean } = {},
  ): Observable<ReglaAlerta[]> {
    return this.http.get<ReglaAlerta[]>(`${this.base}/alerts/rules`, { params: this.aParams(filtro) });
  }

  crearRegla(regla: GuardarRegla): Observable<ReglaAlerta> {
    return this.http.post<ReglaAlerta>(`${this.base}/alerts/rules`, regla);
  }

  actualizarRegla(id: number, regla: GuardarRegla): Observable<ReglaAlerta> {
    return this.http.put<ReglaAlerta>(`${this.base}/alerts/rules/${id}`, regla);
  }

  cambiarEstadoRegla(id: number, activa: boolean): Observable<ReglaAlerta> {
    return this.http.patch<ReglaAlerta>(`${this.base}/alerts/rules/${id}/status`, { activa });
  }

  eliminarRegla(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/alerts/rules/${id}`);
  }

  // --- Alertas ---

  alertasActivas(comunidadId?: number): Observable<Alerta[]> {
    return this.http
      .get<{ nivelGlobal: NivelAlerta; alertas: Alerta[] }>(`${this.base}/alerts/active`, {
        params: this.aParams({ comunidadId }),
      })
      .pipe(map((r) => r.alertas));
  }

  alertas(
    opciones: Paginado &
      RangoFechas & { comunidadId?: number; sensorId?: number; fenomeno?: TipoFenomeno; nivel?: NivelAlerta; estado?: EstadoAlerta },
  ): Observable<Pagina<Alerta>> {
    return this.http.get<Pagina<Alerta>>(`${this.base}/alerts`, { params: this.aParams(opciones) });
  }

  alerta(id: number): Observable<Alerta> {
    return this.http.get<Alerta>(`${this.base}/alerts/${id}`);
  }

  atenderAlerta(id: number, comentario?: string): Observable<Alerta> {
    return this.http.post<Alerta>(`${this.base}/alerts/${id}/attend`, { comentario });
  }

  cerrarAlerta(id: number, comentario?: string): Observable<Alerta> {
    return this.http.post<Alerta>(`${this.base}/alerts/${id}/close`, { comentario });
  }

  // --- Historial y bitácora ---

  historial(
    opciones: Paginado & RangoFechas & { comunidadId?: number; fenomeno?: TipoFenomeno; nivel?: NivelAlerta; estado?: EstadoAlerta },
  ): Observable<Pagina<Evento>> {
    return this.http.get<Pagina<Evento>>(`${this.base}/alerts/events`, { params: this.aParams(opciones) });
  }

  estadisticasHistorial(
    opciones: RangoFechas & { comunidadId?: number; fenomeno?: TipoFenomeno; nivel?: NivelAlerta },
  ): Observable<EstadisticasHistorial> {
    return this.http.get<EstadisticasHistorial>(`${this.base}/alerts/events/stats`, { params: this.aParams(opciones) });
  }

  /** Bitácora del Audit Service. El detalle llega como objeto y la página lo espera como texto JSON. */
  bitacora(
    opciones: Paginado & RangoFechas & { usuario?: string; accion?: string; entidad?: string; servicio?: string },
  ): Observable<Pagina<RegistroBitacora>> {
    return this.http
      .get<Pagina<Omit<RegistroBitacora, 'detalle'> & { detalle: unknown }>>(`${this.base}/audit/events`, {
        params: this.aParams(opciones),
      })
      .pipe(
        map((p) => ({
          ...p,
          elementos: p.elementos.map((e) => ({
            ...e,
            detalle: e.detalle == null ? null : typeof e.detalle === 'string' ? e.detalle : JSON.stringify(e.detalle),
          })),
        })),
      );
  }

  catalogosBitacora(): Observable<{ acciones: string[]; entidades: string[]; servicios: string[] }> {
    return this.http.get<{ acciones: string[]; entidades: string[]; servicios: string[] }>(`${this.base}/audit/catalogs`);
  }

  // --- Cuentas ---

  cerrarSesion(): Observable<void> {
    return this.http.post<void>(`${this.base}/auth/logout`, null);
  }

  usuarios(filtro: { busqueda?: string; rol?: RolUsuario; activo?: boolean } = {}): Observable<Usuario[]> {
    return this.http.get<Usuario[]>(`${this.base}/auth/users`, { params: this.aParams(filtro) });
  }

  crearUsuario(usuario: RegistroUsuario): Observable<Usuario> {
    return this.http.post<Usuario>(`${this.base}/auth/users`, usuario);
  }

  actualizarUsuario(id: number, cambios: ActualizarUsuario): Observable<Usuario> {
    return this.http.put<Usuario>(`${this.base}/auth/users/${id}`, cambios);
  }

  cambiarEstadoUsuario(id: number, activo: boolean): Observable<Usuario> {
    return this.http.patch<Usuario>(`${this.base}/auth/users/${id}/status`, { activo });
  }

  restablecerPassword(id: number, passwordNueva: string): Observable<void> {
    return this.http.post<void>(`${this.base}/auth/users/${id}/password`, { passwordNueva });
  }

  reiniciarSistema(): Observable<{ mensaje: string }> {
    return this.http.post<{ mensaje: string }>(`${this.base}/sensors/system/reset`, null);
  }

  // --- Reportes (Report Service) ---

  tiposReporte(): Observable<TipoReporte[]> {
    return this.http.get<TipoReporte[]>(`${this.base}/reports/types`);
  }

  solicitarReporte(solicitud: SolicitudReporte): Observable<Reporte> {
    return this.http.post<Reporte>(`${this.base}/reports`, solicitud);
  }

  reporte(reportId: string): Observable<Reporte> {
    return this.http.get<Reporte>(`${this.base}/reports/${reportId}`);
  }

  historialReportes(opciones: Paginado & { type?: string; status?: string } = {}): Observable<Pagina<Reporte>> {
    return this.http.get<Pagina<Reporte>>(`${this.base}/reports`, { params: this.aParams(opciones) });
  }

  /** El PDF llega como binario: el componente lo guarda con el nombre que indica el servidor. */
  descargarReporte(reportId: string): Observable<Blob> {
    return this.http.get(`${this.base}/reports/${reportId}/download`, { responseType: 'blob' });
  }

  // --- Auxiliares ---

  private plantillasUmbrales(): Observable<PlantillasUmbrales | null> {
    return this.http.get<PlantillasUmbrales>(`${this.base}/alerts/thresholds/templates`).pipe(catchError(() => of(null)));
  }

  private umbralesDe(calibrados: UmbralesAlert | undefined, plantillas: PlantillasUmbrales | null, tipo: TipoSensor): Umbrales {
    if (calibrados) {
      const { sensorId: _id, tipoSensor: _tipo, ...umbrales } = calibrados;
      return { ...SIN_UMBRALES, ...umbrales };
    }
    const p = plantillas?.[tipo];
    if (!p) return SIN_UMBRALES;
    return {
      umbralAmarilloAlto: p.amarilloAlto,
      umbralNaranjaAlto: p.naranjaAlto,
      umbralRojoAlto: p.rojoAlto,
      umbralAmarilloBajo: p.amarilloBajo,
      umbralNaranjaBajo: p.naranjaBajo,
      umbralRojoBajo: p.rojoBajo,
    };
  }

  private separarUmbrales<T extends Partial<Umbrales>>(datos: T): { umbrales: Partial<Umbrales>; resto: Omit<T, keyof Umbrales> } {
    const {
      umbralAmarilloAlto, umbralNaranjaAlto, umbralRojoAlto, umbralAmarilloBajo, umbralNaranjaBajo, umbralRojoBajo, ...resto
    } = datos;
    return {
      umbrales: { umbralAmarilloAlto, umbralNaranjaAlto, umbralRojoAlto, umbralAmarilloBajo, umbralNaranjaBajo, umbralRojoBajo },
      resto,
    };
  }

  /**
   * Guarda la calibración en Alert Service si el formulario trajo umbrales. Si coinciden
   * con la plantilla del tipo no se guarda nada: el sensor sigue la plantilla.
   */
  private guardarUmbrales(sensor: Sensor, umbrales: Partial<Umbrales>): Observable<Sensor> {
    const definidos = Object.values(umbrales).some((v) => v !== undefined);
    if (!definidos) return of(this.normalizarSensor(sensor));

    return this.plantillasUmbrales().pipe(
      switchMap((plantillas) => {
        const recomendados = this.umbralesDe(undefined, plantillas, sensor.tipo);
        const iguales = (Object.keys(recomendados) as (keyof Umbrales)[]).every(
          (k) => (umbrales[k] ?? null) === recomendados[k],
        );
        const peticion = iguales
          ? this.http.delete<void>(`${this.base}/alerts/thresholds/${sensor.id}`).pipe(map(() => recomendados))
          : this.http
              .put<UmbralesAlert>(`${this.base}/alerts/thresholds/${sensor.id}`, {
                tipoSensor: sensor.tipo,
                ...SIN_UMBRALES,
                ...Object.fromEntries(Object.entries(umbrales).map(([k, v]) => [k, v ?? null])),
              })
              .pipe(map((u) => this.umbralesDe(u, plantillas, sensor.tipo)));
        return peticion.pipe(map((u) => this.normalizarSensor({ ...sensor, ...u })));
      }),
    );
  }

  /**
   * Si Alert Service no pudo evaluar, el nivel llega nulo. El tablero colorea por nivel y
   * necesita un valor: se muestra como normal hasta la siguiente evaluación.
   */
  private normalizarSensor(sensor: Sensor): Sensor {
    return { ...SIN_UMBRALES, ...sensor, nivelActual: sensor.nivelActual ?? 'Verde' };
  }

  /** Convierte un objeto de filtros a query string omitiendo lo que no se especificó. */
  private aParams(opciones: object): HttpParams {
    let params = new HttpParams();

    for (const [clave, valor] of Object.entries(opciones)) {
      if (valor !== undefined && valor !== null && valor !== '') {
        params = params.set(clave, String(valor));
      }
    }

    return params;
  }
}

import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
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
  LecturaHistorica,
  NivelAlerta,
  Pagina,
  PlantillaSensor,
  ReglaAlerta,
  RegistroUsuario,
  RegistroBitacora,
  ResumenDashboard,
  RolUsuario,
  SerieHistorica,
  Sensor,
  TipoFenomeno,
  TipoSensor,
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

/** Cliente HTTP de la API. Un único punto donde vive la forma de cada extremo. */
@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);
  private readonly base = environment.urlApi;

  // --- Monitoreo ---

  comunidades(): Observable<Comunidad[]> {
    return this.http.get<Comunidad[]>(`${this.base}/monitoreo/comunidades`);
  }

  estadoComunidad(comunidadId: number): Observable<EstadoComunidad> {
    return this.http.get<EstadoComunidad>(
      `${this.base}/monitoreo/comunidades/${comunidadId}/estado`,
    );
  }

  series(comunidadId: number, minutos = 30): Observable<SerieHistorica[]> {
    return this.http.get<SerieHistorica[]>(
      `${this.base}/monitoreo/comunidades/${comunidadId}/series`,
      { params: new HttpParams().set('minutos', minutos) },
    );
  }

  resumen(comunidadId?: number): Observable<ResumenDashboard> {
    let params = new HttpParams();
    if (comunidadId !== undefined) params = params.set('comunidadId', comunidadId);
    return this.http.get<ResumenDashboard>(`${this.base}/monitoreo/resumen`, { params });
  }

  // --- Comunidades ---

  listarComunidades(filtro: {
    busqueda?: string;
    activa?: boolean;
    municipio?: string;
    departamento?: string;
  } = {}): Observable<Comunidad[]> {
    return this.http.get<Comunidad[]>(`${this.base}/comunidades`, { params: this.aParams(filtro) });
  }

  crearComunidad(datos: GuardarComunidad): Observable<Comunidad> {
    return this.http.post<Comunidad>(`${this.base}/comunidades`, datos);
  }

  actualizarComunidad(id: number, datos: GuardarComunidad): Observable<Comunidad> {
    return this.http.put<Comunidad>(`${this.base}/comunidades/${id}`, datos);
  }

  cambiarEstadoComunidad(id: number, activa: boolean): Observable<Comunidad> {
    return this.http.patch<Comunidad>(`${this.base}/comunidades/${id}/estado`, null, {
      params: new HttpParams().set('activa', activa),
    });
  }

  // --- Sensores ---

  sensores(filtro: {
    comunidadId?: number;
    tipo?: TipoSensor;
    estado?: EstadoSensor;
    codigo?: string;
  } = {}): Observable<Sensor[]> {
    return this.http.get<Sensor[]>(`${this.base}/sensores`, { params: this.aParams(filtro) });
  }

  plantillasSensor(): Observable<PlantillaSensor[]> {
    return this.http.get<PlantillaSensor[]>(`${this.base}/sensores/plantillas`);
  }

  crearSensor(sensor: CrearSensor): Observable<Sensor> {
    return this.http.post<Sensor>(`${this.base}/sensores`, sensor);
  }

  actualizarSensor(id: number, cambios: ActualizarSensor): Observable<Sensor> {
    return this.http.put<Sensor>(`${this.base}/sensores/${id}`, cambios);
  }

  cambiarEstadoSensor(id: number, estado: EstadoSensor): Observable<Sensor> {
    return this.http.patch<Sensor>(`${this.base}/sensores/${id}/estado`, null, {
      params: new HttpParams().set('estado', estado),
    });
  }

  establecerValorSensor(id: number, valor: number): Observable<Sensor> {
    return this.http.post<Sensor>(`${this.base}/sensores/${id}/valor`, { valor });
  }

  eliminarSensor(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/sensores/${id}`);
  }

  // --- Lecturas ---

  lecturas(
    filtro: Paginado & RangoFechas & { sensorId?: number; comunidadId?: number; tipo?: TipoSensor },
  ): Observable<Pagina<LecturaHistorica>> {
    return this.http.get<Pagina<LecturaHistorica>>(`${this.base}/lecturas`, {
      params: this.aParams(filtro),
    });
  }

  registrarLectura(sensorId: number, valor: number): Observable<LecturaHistorica> {
    return this.http.post<LecturaHistorica>(`${this.base}/lecturas`, { sensorId, valor });
  }

  eliminarLectura(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/lecturas/${id}`);
  }

  // --- Reglas de alerta ---

  reglas(filtro: {
    busqueda?: string;
    tipoSensor?: TipoSensor;
    fenomeno?: TipoFenomeno;
    nivel?: NivelAlerta;
    activa?: boolean;
  } = {}): Observable<ReglaAlerta[]> {
    return this.http.get<ReglaAlerta[]>(`${this.base}/reglas`, { params: this.aParams(filtro) });
  }

  crearRegla(regla: GuardarRegla): Observable<ReglaAlerta> {
    return this.http.post<ReglaAlerta>(`${this.base}/reglas`, regla);
  }

  actualizarRegla(id: number, regla: GuardarRegla): Observable<ReglaAlerta> {
    return this.http.put<ReglaAlerta>(`${this.base}/reglas/${id}`, regla);
  }

  cambiarEstadoRegla(id: number, activa: boolean): Observable<ReglaAlerta> {
    return this.http.patch<ReglaAlerta>(`${this.base}/reglas/${id}/estado`, null, {
      params: new HttpParams().set('activa', activa),
    });
  }

  eliminarRegla(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/reglas/${id}`);
  }

  // --- Alertas ---

  alertasActivas(comunidadId?: number): Observable<Alerta[]> {
    let params = new HttpParams();
    if (comunidadId !== undefined) params = params.set('comunidadId', comunidadId);
    return this.http.get<Alerta[]>(`${this.base}/alertas/activas`, { params });
  }

  alertas(
    opciones: Paginado &
      RangoFechas & {
        comunidadId?: number;
        sensorId?: number;
        fenomeno?: TipoFenomeno;
        nivel?: NivelAlerta;
        estado?: EstadoAlerta;
      },
  ): Observable<Pagina<Alerta>> {
    return this.http.get<Pagina<Alerta>>(`${this.base}/alertas`, {
      params: this.aParams(opciones),
    });
  }

  alerta(id: number): Observable<Alerta> {
    return this.http.get<Alerta>(`${this.base}/alertas/${id}`);
  }

  atenderAlerta(id: number, comentario?: string): Observable<Alerta> {
    return this.http.post<Alerta>(`${this.base}/alertas/${id}/atender`, { comentario });
  }

  cerrarAlerta(id: number, comentario?: string): Observable<Alerta> {
    return this.http.post<Alerta>(`${this.base}/alertas/${id}/cerrar`, { comentario });
  }

  // --- Historial y bitácora ---

  historial(
    opciones: Paginado &
      RangoFechas & {
        comunidadId?: number;
        fenomeno?: TipoFenomeno;
        nivel?: NivelAlerta;
        estado?: EstadoAlerta;
      },
  ): Observable<Pagina<Evento>> {
    return this.http.get<Pagina<Evento>>(`${this.base}/historial`, {
      params: this.aParams(opciones),
    });
  }

  estadisticasHistorial(
    opciones: RangoFechas & { comunidadId?: number; fenomeno?: TipoFenomeno; nivel?: NivelAlerta },
  ): Observable<EstadisticasHistorial> {
    return this.http.get<EstadisticasHistorial>(`${this.base}/historial/estadisticas`, {
      params: this.aParams(opciones),
    });
  }

  bitacora(
    opciones: Paginado & RangoFechas & { usuario?: string; accion?: string; entidad?: string },
  ): Observable<Pagina<RegistroBitacora>> {
    return this.http.get<Pagina<RegistroBitacora>>(`${this.base}/bitacora`, {
      params: this.aParams(opciones),
    });
  }

  catalogosBitacora(): Observable<{ acciones: string[]; entidades: string[] }> {
    return this.http.get<{ acciones: string[]; entidades: string[] }>(
      `${this.base}/bitacora/catalogos`,
    );
  }

  // --- Cuentas ---

  cerrarSesion(): Observable<void> {
    return this.http.post<void>(`${this.base}/cuenta/logout`, null);
  }

  usuarios(filtro: { busqueda?: string; rol?: RolUsuario; activo?: boolean } = {}): Observable<Usuario[]> {
    return this.http.get<Usuario[]>(`${this.base}/cuenta/usuarios`, { params: this.aParams(filtro) });
  }

  crearUsuario(usuario: RegistroUsuario): Observable<Usuario> {
    return this.http.post<Usuario>(`${this.base}/cuenta/usuarios`, usuario);
  }

  actualizarUsuario(id: number, cambios: ActualizarUsuario): Observable<Usuario> {
    return this.http.put<Usuario>(`${this.base}/cuenta/usuarios/${id}`, cambios);
  }

  cambiarEstadoUsuario(id: number, activo: boolean): Observable<Usuario> {
    return this.http.patch<Usuario>(`${this.base}/cuenta/usuarios/${id}/estado`, null, {
      params: new HttpParams().set('activo', activo),
    });
  }

  restablecerPassword(id: number, passwordNueva: string): Observable<void> {
    return this.http.post<void>(`${this.base}/cuenta/usuarios/${id}/password`, { passwordNueva });
  }

  reiniciarSistema(): Observable<{ mensaje: string }> {
    return this.http.post<{ mensaje: string }>(`${this.base}/sistema/reiniciar`, null);
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

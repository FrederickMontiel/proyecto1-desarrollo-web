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
  EstadoComunidad,
  Evento,
  EstadoSensor,
  NivelAlerta,
  Pagina,
  PlantillaSensor,
  RegistroUsuario,
  RegistroBitacora,
  ResumenDashboard,
  SerieHistorica,
  Sensor,
  TipoFenomeno,
  Usuario,
} from '../modelos/modelos';

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

  // --- Sensores ---

  sensores(comunidadId?: number): Observable<Sensor[]> {
    let params = new HttpParams();
    if (comunidadId !== undefined) params = params.set('comunidadId', comunidadId);
    return this.http.get<Sensor[]>(`${this.base}/sensores`, { params });
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

  // --- Alertas ---

  alertasActivas(comunidadId?: number): Observable<Alerta[]> {
    let params = new HttpParams();
    if (comunidadId !== undefined) params = params.set('comunidadId', comunidadId);
    return this.http.get<Alerta[]>(`${this.base}/alertas/activas`, { params });
  }

  alertas(opciones: {
    pagina?: number;
    tamano?: number;
    comunidadId?: number;
    nivel?: NivelAlerta;
  }): Observable<Pagina<Alerta>> {
    return this.http.get<Pagina<Alerta>>(`${this.base}/alertas`, {
      params: this.aParams(opciones),
    });
  }

  reconocerAlerta(id: number): Observable<Alerta> {
    return this.http.post<Alerta>(`${this.base}/alertas/${id}/reconocer`, null);
  }

  // --- Historial y bitácora ---

  historial(opciones: {
    pagina?: number;
    tamano?: number;
    comunidadId?: number;
    fenomeno?: TipoFenomeno;
    desde?: string;
    hasta?: string;
  }): Observable<Pagina<Evento>> {
    return this.http.get<Pagina<Evento>>(`${this.base}/historial`, {
      params: this.aParams(opciones),
    });
  }

  bitacora(opciones: {
    pagina?: number;
    tamano?: number;
    accion?: string;
  }): Observable<Pagina<RegistroBitacora>> {
    return this.http.get<Pagina<RegistroBitacora>>(`${this.base}/bitacora`, {
      params: this.aParams(opciones),
    });
  }

  // --- Administración ---

  usuarios(): Observable<Usuario[]> {
    return this.http.get<Usuario[]>(`${this.base}/cuenta/usuarios`);
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
  private aParams(opciones: Record<string, unknown>): HttpParams {
    let params = new HttpParams();

    for (const [clave, valor] of Object.entries(opciones)) {
      if (valor !== undefined && valor !== null && valor !== '') {
        params = params.set(clave, String(valor));
      }
    }

    return params;
  }
}

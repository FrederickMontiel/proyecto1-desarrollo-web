import { Injectable, inject, signal } from '@angular/core';
import {
  HttpTransportType,
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Alerta, EstadoComunidad, Lectura, Sensor } from '../modelos/modelos';
import { Autenticacion } from './autenticacion';

export type EstadoConexion = 'desconectado' | 'conectando' | 'conectado' | 'reconectando';

/**
 * Canal en tiempo real con la API. Abre un WebSocket permanente contra el hub y expone
 * cada mensaje del servidor como un flujo independiente.
 *
 * Se fuerza el transporte WebSocket y se omite la negociación previa: el servidor solo
 * acepta ese transporte, así que la petición de negociación sería un viaje de ida y vuelta
 * inútil antes de abrir el socket.
 */
@Injectable({ providedIn: 'root' })
export class TiempoReal {
  private readonly auth = inject(Autenticacion);

  private conexion: HubConnection | null = null;
  private comunidadSuscrita: number | null = null;

  readonly estado = signal<EstadoConexion>('desconectado');
  readonly ultimoError = signal<string | null>(null);

  readonly lectura$ = new Subject<Lectura>();
  readonly alertaGenerada$ = new Subject<Alerta>();
  readonly alertaCerrada$ = new Subject<Alerta>();
  readonly sensorCambiado$ = new Subject<Sensor>();
  readonly estadoComunidad$ = new Subject<EstadoComunidad>();
  readonly sistemaReiniciado$ = new Subject<void>();

  async conectar(): Promise<void> {
    if (this.conexion && this.conexion.state !== HubConnectionState.Disconnected) return;

    const conexion = new HubConnectionBuilder()
      .withUrl(environment.urlHub, {
        // El navegador no deja poner cabeceras en el handshake de un WebSocket, así que
        // el token viaja como query string; la API lo recoge en OnMessageReceived.
        accessTokenFactory: () => this.auth.token ?? '',
        transport: HttpTransportType.WebSockets,
        skipNegotiation: true,
      })
      // Reintentos escalonados: rápido al principio, porque un corte breve de red no debe
      // dejar el tablero de alertas a ciegas más de unos segundos.
      .withAutomaticReconnect([0, 1000, 3000, 5000, 10000, 15000])
      .configureLogging(environment.produccion ? LogLevel.Warning : LogLevel.Information)
      .build();

    this.registrarManejadores(conexion);

    conexion.onreconnecting(() => this.estado.set('reconectando'));

    conexion.onreconnected(async () => {
      this.estado.set('conectado');
      this.ultimoError.set(null);

      // Los grupos viven en la conexión: al reconectar hay que volver a suscribirse o el
      // cliente dejaría de recibir los datos de su comunidad sin ninguna señal de error.
      if (this.comunidadSuscrita !== null) {
        await conexion.invoke('SuscribirComunidad', this.comunidadSuscrita);
      }
    });

    conexion.onclose((error) => {
      this.estado.set('desconectado');
      if (error) this.ultimoError.set(error.message);
    });

    this.conexion = conexion;
    this.estado.set('conectando');

    try {
      await conexion.start();
      this.estado.set('conectado');
      this.ultimoError.set(null);
    } catch (error) {
      this.estado.set('desconectado');
      this.ultimoError.set(error instanceof Error ? error.message : 'No se pudo abrir el canal.');
      throw error;
    }
  }

  async suscribirComunidad(comunidadId: number): Promise<void> {
    if (this.comunidadSuscrita === comunidadId) return;

    await this.conectar();

    if (this.comunidadSuscrita !== null) {
      await this.conexion!.invoke('CancelarSuscripcion', this.comunidadSuscrita);
    }

    await this.conexion!.invoke('SuscribirComunidad', comunidadId);
    this.comunidadSuscrita = comunidadId;
  }

  async desconectar(): Promise<void> {
    this.comunidadSuscrita = null;
    await this.conexion?.stop();
    this.conexion = null;
    this.estado.set('desconectado');
  }

  private registrarManejadores(conexion: HubConnection): void {
    conexion.on('LecturaRecibida', (lectura: Lectura) => this.lectura$.next(lectura));
    conexion.on('AlertaGenerada', (alerta: Alerta) => this.alertaGenerada$.next(alerta));
    conexion.on('AlertaCerrada', (alerta: Alerta) => this.alertaCerrada$.next(alerta));
    conexion.on('EstadoSensorCambiado', (sensor: Sensor) => this.sensorCambiado$.next(sensor));
    conexion.on('EstadoComunidad', (estado: EstadoComunidad) => this.estadoComunidad$.next(estado));
    conexion.on('SistemaReiniciado', () => this.sistemaReiniciado$.next());
  }
}

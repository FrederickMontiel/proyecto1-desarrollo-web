import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  Alerta,
  Comunidad,
  Lectura,
  NIVELES,
  NivelAlerta,
  PuntoSerie,
  ResumenDashboard,
  Sensor,
  TipoSensor,
} from '../modelos/modelos';
import { Api } from './api';
import { Notificaciones } from './notificaciones';
import { TiempoReal } from './tiempo-real';

/** Muestras que conserva cada sensor en memoria para dibujar su curva de evolución. */
const MAXIMO_PUNTOS_SERIE = 120;

/**
 * Estado vivo del monitoreo. Es la única fuente de verdad del tablero: la carga inicial
 * llega por HTTP y a partir de ahí todo se actualiza con los mensajes del WebSocket, sin
 * volver a consultar la API.
 */
@Injectable({ providedIn: 'root' })
export class EstadoMonitoreo {
  private readonly api = inject(Api);
  private readonly tiempoReal = inject(TiempoReal);
  private readonly notificaciones = inject(Notificaciones);

  private suscrito = false;

  readonly comunidades = signal<Comunidad[]>([]);
  readonly comunidadActiva = signal<Comunidad | null>(null);
  readonly sensores = signal<Sensor[]>([]);
  readonly alertasActivas = signal<Alerta[]>([]);
  readonly resumen = signal<ResumenDashboard | null>(null);
  readonly cargando = signal(false);

  /** Series en memoria, indexadas por sensor. */
  readonly series = signal<Map<number, PuntoSerie[]>>(new Map());

  readonly estadoConexion = this.tiempoReal.estado;

  /** Nivel más severo entre las alertas abiertas: es el color del tablero completo. */
  readonly nivelGlobal = computed<NivelAlerta>(() => {
    const alertas = this.alertasActivas();
    if (alertas.length === 0) return 'Verde';

    return alertas.reduce<NivelAlerta>(
      (peor, alerta) => (NIVELES[alerta.nivel].orden > NIVELES[peor].orden ? alerta.nivel : peor),
      'Verde',
    );
  });

  readonly sensoresActivos = computed(() => this.sensores().filter((s) => s.estado === 'Activo'));

  readonly alertasSinReconocer = computed(() =>
    this.alertasActivas().filter((a) => !a.reconocida),
  );

  /** Devuelve el sensor de una magnitud concreta, que es como los consulta el tablero. */
  sensorPorTipo(tipo: TipoSensor): Sensor | undefined {
    return this.sensores().find((s) => s.tipo === tipo);
  }

  /**
   * Arranca el canal en tiempo real y engancha los flujos al estado.
   *
   * El enganche de los flujos ocurre una sola vez en la vida de la aplicación; la
   * conexión, en cambio, se asegura en cada llamada, porque cerrar sesión la cierra y
   * al volver a entrar hay que abrirla de nuevo.
   */
  async iniciar(): Promise<void> {
    if (this.suscrito) {
      await this.tiempoReal.conectar();
      return;
    }

    this.suscrito = true;

    this.tiempoReal.lectura$.subscribe((lectura) => this.aplicarLectura(lectura));
    this.tiempoReal.sensorCambiado$.subscribe((sensor) => this.aplicarSensor(sensor));

    this.tiempoReal.alertaGenerada$.subscribe((alerta) => {
      const existente = this.alertasActivas().find((a) => a.id === alerta.id);

      this.alertasActivas.update((lista) => [
        alerta,
        ...lista.filter((a) => a.id !== alerta.id),
      ]);

      // Solo suena cuando la alerta es nueva o subió de nivel. Un simple acuse de recibo
      // vuelve a llegar por el mismo canal y no debe disparar la sirena otra vez.
      const esNovedad =
        !existente || NIVELES[alerta.nivel].orden > NIVELES[existente.nivel].orden;

      if (esNovedad) this.notificaciones.notificarAlerta(alerta);
    });

    this.tiempoReal.alertaCerrada$.subscribe((alerta) => {
      this.alertasActivas.update((lista) => lista.filter((a) => a.id !== alerta.id));
      this.notificaciones.informar(
        'Situación normalizada',
        `Se cerró la alerta de ${alerta.fenomenoNombre} en ${alerta.comunidadNombre}.`,
      );
    });

    this.tiempoReal.estadoComunidad$.subscribe((estado) => {
      this.sensores.set(estado.sensores);
      this.alertasActivas.set(estado.alertasActivas);
    });

    this.tiempoReal.sistemaReiniciado$.subscribe(() => {
      this.alertasActivas.set([]);
      this.series.set(new Map());
      this.notificaciones.informar(
        'Sistema reiniciado',
        'Se cerraron las alertas activas y se limpiaron las lecturas acumuladas.',
      );
      void this.recargar();
    });

    await this.tiempoReal.conectar();
  }

  /** Cierra el canal y descarta el estado cargado. Se invoca al cerrar sesión. */
  async detener(): Promise<void> {
    await this.tiempoReal.desconectar();

    this.comunidadActiva.set(null);
    this.sensores.set([]);
    this.alertasActivas.set([]);
    this.resumen.set(null);
    this.series.set(new Map());
  }

  /** Carga el catálogo de comunidades y selecciona la primera si no hay ninguna activa. */
  async cargarComunidades(): Promise<void> {
    const comunidades = await firstValueFrom(this.api.comunidades());
    this.comunidades.set(comunidades);

    if (!this.comunidadActiva() && comunidades.length > 0) {
      await this.seleccionarComunidad(comunidades[0]);
    }
  }

  async seleccionarComunidad(comunidad: Comunidad): Promise<void> {
    this.comunidadActiva.set(comunidad);
    await this.recargar();
    await this.tiempoReal.suscribirComunidad(comunidad.id);
  }

  /** Vuelve a pedir a la API la fotografía completa de la comunidad activa. */
  async recargar(): Promise<void> {
    const comunidad = this.comunidadActiva();
    if (!comunidad) return;

    this.cargando.set(true);

    try {
      const [estado, resumen, series] = await Promise.all([
        firstValueFrom(this.api.estadoComunidad(comunidad.id)),
        firstValueFrom(this.api.resumen(comunidad.id)),
        firstValueFrom(this.api.series(comunidad.id, 30)),
      ]);

      this.sensores.set(estado.sensores);
      this.alertasActivas.set(estado.alertasActivas);
      this.resumen.set(resumen);

      const mapa = new Map<number, PuntoSerie[]>();
      for (const serie of series) {
        mapa.set(serie.sensorId, serie.puntos.slice(-MAXIMO_PUNTOS_SERIE));
      }
      this.series.set(mapa);
    } finally {
      this.cargando.set(false);
    }
  }

  marcarReconocida(alerta: Alerta): void {
    this.alertasActivas.update((lista) =>
      lista.map((a) => (a.id === alerta.id ? alerta : a)),
    );
  }

  private aplicarLectura(lectura: Lectura): void {
    this.sensores.update((lista) =>
      lista.map((sensor) =>
        sensor.id === lectura.sensorId
          ? {
              ...sensor,
              valorActual: lectura.valor,
              ultimaLectura: lectura.fechaHora,
              nivelActual: lectura.nivel,
            }
          : sensor,
      ),
    );

    this.series.update((mapa) => {
      // Copia superficial del Map para que el signal detecte el cambio: mutar el mismo
      // objeto dejaría la vista sin repintar.
      const copia = new Map(mapa);
      const puntos = [...(copia.get(lectura.sensorId) ?? []), {
        fechaHora: lectura.fechaHora,
        valor: lectura.valor,
      }];

      copia.set(lectura.sensorId, puntos.slice(-MAXIMO_PUNTOS_SERIE));
      return copia;
    });
  }

  private aplicarSensor(sensor: Sensor): void {
    this.sensores.update((lista) => {
      const existe = lista.some((s) => s.id === sensor.id);
      return existe ? lista.map((s) => (s.id === sensor.id ? sensor : s)) : [...lista, sensor];
    });
  }
}

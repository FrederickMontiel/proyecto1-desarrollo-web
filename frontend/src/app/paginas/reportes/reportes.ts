import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { Paginador } from '../../componentes/paginador/paginador';
import {
  CodigoReporte,
  Comunidad,
  ESTADOS_ALERTA,
  EstadoAlerta,
  EstadoReporte,
  EstadoSensor,
  FENOMENOS,
  FENOMENOS_PROTOCOLO,
  FiltroReporte,
  NIVELES,
  NivelAlerta,
  ORDEN_TIPOS_SENSOR,
  Pagina,
  Reporte,
  SENSORES,
  Sensor,
  SolicitudReporte,
  TipoFenomeno,
  TipoReporte,
  TipoSensor,
  fechaIso,
} from '../../core/modelos/modelos';
import { Api } from '../../core/servicios/api';
import { Autenticacion } from '../../core/servicios/autenticacion';
import { Notificaciones } from '../../core/servicios/notificaciones';

/** Iconos de cada reporte, solo para la presentación del catálogo. */
const ICONOS: Record<CodigoReporte, string> = {
  COMMUNITIES: '🏘️',
  SENSORS: '🛰️',
  READINGS: '📈',
  ALERTS: '🚨',
  EVENT_HISTORY: '🗂️',
  SENSOR_STATUS: '📶',
  CLIMATE_STATS: '🌡️',
  EXECUTIVE: '📋',
};

const CLASE_ESTADO: Record<EstadoReporte, string> = {
  PENDING: 'nivel-amarillo',
  PROCESSING: 'nivel-amarillo',
  COMPLETED: 'nivel-verde',
  FAILED: 'nivel-rojo',
};

/**
 * Reportería (sección 14 del documento): elegir un reporte, aplicar filtros, generarlo,
 * descargar el PDF y consultar el historial.
 *
 * El PDF lo genera el Report Service en el backend (RNF-REP-01). La generación es
 * asíncrona: la solicitud vuelve de inmediato y esta página consulta el estado cada pocos
 * segundos hasta que el reporte queda completado o fallido. La descarga es exclusiva de los
 * administradores (RF-REP-10): el botón solo aparece con el permiso `reportes:descargar`.
 */
@Component({
  selector: 'app-reportes',
  imports: [DatePipe, DecimalPipe, FormsModule, Paginador],
  templateUrl: './reportes.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './reportes.scss',
})
export class Reportes implements OnInit, OnDestroy {
  private readonly api = inject(Api);
  private readonly notificaciones = inject(Notificaciones);
  protected readonly auth = inject(Autenticacion);

  protected readonly tipos = signal<TipoReporte[]>([]);
  protected readonly seleccionado = signal<TipoReporte | null>(null);
  protected readonly comunidades = signal<Comunidad[]>([]);
  protected readonly sensores = signal<Sensor[]>([]);
  protected readonly historial = signal<Pagina<Reporte> | null>(null);
  protected readonly enCurso = signal<Reporte[]>([]);
  protected readonly generando = signal(false);
  protected readonly descargando = signal<string | null>(null);
  protected readonly cargandoHistorial = signal(false);

  // Filtros del formulario. Solo se envían los que admite el reporte elegido.
  protected readonly comunidadId = signal<number | ''>('');
  protected readonly sensorId = signal<number | ''>('');
  protected readonly tipoSensor = signal<TipoSensor | ''>('');
  protected readonly estadoSensor = signal<EstadoSensor | ''>('');
  protected readonly fenomeno = signal<TipoFenomeno | ''>('');
  protected readonly nivel = signal<NivelAlerta | ''>('');
  protected readonly estado = signal<EstadoAlerta | ''>('');
  protected readonly activa = signal<'' | 'true' | 'false'>('');
  protected readonly departamento = signal('');
  protected readonly desde = signal('');
  protected readonly hasta = signal('');

  // Filtros del historial.
  protected readonly paginaHistorial = signal(1);
  protected readonly tipoHistorial = signal<CodigoReporte | ''>('');
  protected readonly estadoHistorial = signal<EstadoReporte | ''>('');

  protected readonly iconos = ICONOS;
  protected readonly claseEstado = CLASE_ESTADO;
  protected readonly tiposSensor = ORDEN_TIPOS_SENSOR.map((t) => ({ valor: t, ...SENSORES[t] }));
  protected readonly estadosSensor: { valor: EstadoSensor; etiqueta: string }[] = [
    { valor: 'Activo', etiqueta: 'Activo' },
    { valor: 'Inactivo', etiqueta: 'Inactivo' },
    { valor: 'SinSenal', etiqueta: 'Sin comunicación' },
  ];
  protected readonly fenomenos = FENOMENOS_PROTOCOLO.map((f) => ({ valor: f, ...FENOMENOS[f] }));
  protected readonly niveles = Object.keys(NIVELES) as NivelAlerta[];
  protected readonly estadosAlerta = Object.keys(ESTADOS_ALERTA) as EstadoAlerta[];
  protected readonly estadosReporte: { valor: EstadoReporte; etiqueta: string }[] = [
    { valor: 'COMPLETED', etiqueta: 'Completado' },
    { valor: 'PROCESSING', etiqueta: 'En proceso' },
    { valor: 'PENDING', etiqueta: 'Pendiente' },
    { valor: 'FAILED', etiqueta: 'Fallido' },
  ];

  /** Sensores de la comunidad elegida (o todos), para el filtro de sensor. */
  protected readonly sensoresFiltrados = computed(() => {
    const comunidad = this.comunidadId();
    return comunidad ? this.sensores().filter((s) => s.comunidadId === comunidad) : this.sensores();
  });

  private sondeo: ReturnType<typeof setInterval> | null = null;

  async ngOnInit(): Promise<void> {
    const [tipos, comunidades, sensores] = await Promise.all([
      firstValueFrom(this.api.tiposReporte()),
      firstValueFrom(this.api.listarComunidades()),
      firstValueFrom(this.api.sensores()),
    ]);
    this.tipos.set(tipos);
    this.comunidades.set(comunidades);
    this.sensores.set(sensores);
    this.seleccionado.set(tipos[0] ?? null);
    await this.cargarHistorial();
  }

  ngOnDestroy(): void {
    this.detenerSondeo();
  }

  protected admite(filtro: FiltroReporte): boolean {
    return this.seleccionado()?.filters.includes(filtro) ?? false;
  }

  protected elegir(tipo: TipoReporte): void {
    this.seleccionado.set(tipo);
  }

  protected limpiarFiltros(): void {
    this.comunidadId.set('');
    this.sensorId.set('');
    this.tipoSensor.set('');
    this.estadoSensor.set('');
    this.fenomeno.set('');
    this.nivel.set('');
    this.estado.set('');
    this.activa.set('');
    this.departamento.set('');
    this.desde.set('');
    this.hasta.set('');
  }

  /** Arma los filtros con solo lo que el reporte admite y el usuario llenó. */
  private filtros(): SolicitudReporte['filters'] {
    const valores: Partial<Record<FiltroReporte, string | number | boolean | undefined>> = {
      comunidadId: this.comunidadId() || undefined,
      sensorId: this.sensorId() || undefined,
      tipoSensor: this.tipoSensor() || undefined,
      estadoSensor: this.estadoSensor() || undefined,
      fenomeno: this.fenomeno() || undefined,
      nivel: this.nivel() || undefined,
      estado: this.estado() || undefined,
      activa: this.activa() === '' ? undefined : this.activa() === 'true',
      departamento: this.departamento().trim() || undefined,
      desde: fechaIso(this.desde()),
      hasta: fechaIso(this.hasta()),
    };
    const filtros: SolicitudReporte['filters'] = {};
    for (const [clave, valor] of Object.entries(valores) as [FiltroReporte, string | number | boolean | undefined][]) {
      if (valor !== undefined && this.admite(clave)) filtros[clave] = valor;
    }
    return filtros;
  }

  protected async generar(): Promise<void> {
    const tipo = this.seleccionado();
    if (!tipo || this.generando()) return;

    this.generando.set(true);
    try {
      const reporte = await firstValueFrom(this.api.solicitarReporte({ type: tipo.type, filters: this.filtros() }));
      this.enCurso.update((lista) => [reporte, ...lista]);
      this.notificaciones.informar('Reporte en cola', `${reporte.reportId}: ${tipo.name}. Se genera en segundo plano.`);
      this.iniciarSondeo();
    } finally {
      this.generando.set(false);
    }
  }

  /** Consulta el estado de los reportes en curso hasta que todos terminan. */
  private iniciarSondeo(): void {
    if (this.sondeo) return;
    this.sondeo = setInterval(() => void this.actualizarEnCurso(), 1500);
  }

  private detenerSondeo(): void {
    if (this.sondeo) clearInterval(this.sondeo);
    this.sondeo = null;
  }

  private async actualizarEnCurso(): Promise<void> {
    const pendientes = this.enCurso().filter((r) => r.status === 'PENDING' || r.status === 'PROCESSING');
    if (pendientes.length === 0) {
      this.detenerSondeo();
      return;
    }

    const actualizados = await Promise.all(pendientes.map((r) => firstValueFrom(this.api.reporte(r.reportId))));
    this.enCurso.update((lista) => lista.map((r) => actualizados.find((a) => a.reportId === r.reportId) ?? r));

    for (const r of actualizados) {
      if (r.status === 'COMPLETED') {
        this.notificaciones.informar('Reporte listo', `${r.reportId}: ${r.typeName} (${r.rows ?? 0} registros).`);
      } else if (r.status === 'FAILED') {
        this.notificaciones.error(`${r.reportId}: ${r.error ?? 'la generación falló.'}`);
      }
    }
    if (actualizados.some((r) => r.status === 'COMPLETED' || r.status === 'FAILED')) await this.cargarHistorial();
  }

  protected async descargar(reporte: Reporte): Promise<void> {
    this.descargando.set(reporte.reportId);
    try {
      const pdf = await firstValueFrom(this.api.descargarReporte(reporte.reportId));
      const url = URL.createObjectURL(pdf);
      const enlace = document.createElement('a');
      enlace.href = url;
      enlace.download = reporte.fileName;
      enlace.click();
      // Se libera en el siguiente ciclo: algunos navegadores cancelan la descarga si la URL
      // desaparece antes de que empiece.
      setTimeout(() => URL.revokeObjectURL(url), 0);
      await this.cargarHistorial();
    } finally {
      this.descargando.set(null);
    }
  }

  // ------------------------------------------------------------------ Historial

  protected async cargarHistorial(): Promise<void> {
    this.cargandoHistorial.set(true);
    try {
      this.historial.set(
        await firstValueFrom(
          this.api.historialReportes({
            pagina: this.paginaHistorial(),
            tamano: 15,
            type: this.tipoHistorial() || undefined,
            status: this.estadoHistorial() || undefined,
          }),
        ),
      );
    } finally {
      this.cargandoHistorial.set(false);
    }
  }

  protected async filtrarHistorial(): Promise<void> {
    this.paginaHistorial.set(1);
    await this.cargarHistorial();
  }

  protected async irA(pagina: number): Promise<void> {
    this.paginaHistorial.set(pagina);
    await this.cargarHistorial();
  }

  /** Filtros del reporte en una línea legible: "Comunidad: Aldea El Rosario · Nivel: Rojo". */
  protected describirFiltros(reporte: Reporte): string {
    const nombres: Record<string, string> = {
      comunidadId: 'Comunidad', sensorId: 'Sensor', tipoSensor: 'Tipo', estadoSensor: 'Estado del sensor',
      fenomeno: 'Fenómeno', nivel: 'Nivel', estado: 'Estado', activa: 'Activa', departamento: 'Departamento',
      desde: 'Desde', hasta: 'Hasta',
    };
    const partes = Object.entries(reporte.filters).map(([clave, valor]) => {
      let texto = String(valor);
      if (clave === 'comunidadId') texto = this.comunidades().find((c) => c.id === valor)?.nombre ?? `#${valor}`;
      else if (clave === 'sensorId') texto = this.sensores().find((s) => s.id === valor)?.codigo ?? `#${valor}`;
      else if (clave === 'desde' || clave === 'hasta') texto = new Date(String(valor)).toLocaleString('es-GT');
      else if (clave === 'fenomeno') texto = FENOMENOS[valor as TipoFenomeno]?.etiqueta ?? texto;
      else if (clave === 'tipoSensor') texto = SENSORES[valor as TipoSensor]?.etiqueta ?? texto;
      else if (clave === 'activa') texto = valor ? 'Sí' : 'No';
      return `${nombres[clave] ?? clave}: ${texto}`;
    });
    return partes.length ? partes.join(' · ') : 'Sin filtros';
  }

  protected tamano(bytes?: number): string {
    if (!bytes) return '—';
    return bytes < 1024 * 1024 ? `${Math.round(bytes / 1024)} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }
}

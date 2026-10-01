import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { Paginador } from '../../componentes/paginador/paginador';
import { PanelAlertas } from '../../componentes/panel-alertas/panel-alertas';
import {
  Alerta as AlertaModelo,
  Comunidad,
  ESTADOS_ALERTA,
  EstadoAlerta,
  FENOMENOS,
  FENOMENOS_PROTOCOLO,
  NIVELES,
  NivelAlerta,
  Pagina,
  Sensor,
  TipoFenomeno,
  fechaIso,
} from '../../core/modelos/modelos';
import { Api } from '../../core/servicios/api';
import { Autenticacion } from '../../core/servicios/autenticacion';
import { EstadoMonitoreo } from '../../core/servicios/estado-monitoreo';
import { Notificaciones } from '../../core/servicios/notificaciones';

/**
 * Bandeja de alertas: arriba las que siguen abiertas en la comunidad seleccionada y debajo
 * el registro completo con filtros por fecha, comunidad, sensor, fenómeno, nivel y estado.
 * Cada alerta abre su detalle, desde donde se atiende o se cierra.
 */
@Component({
  selector: 'app-alertas',
  imports: [PanelAlertas, Paginador, DatePipe, DecimalPipe, FormsModule],
  templateUrl: './alertas.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './alertas.scss',
})
export class Alertas implements OnInit {
  private readonly api = inject(Api);
  private readonly notificaciones = inject(Notificaciones);
  protected readonly estado = inject(EstadoMonitoreo);
  protected readonly auth = inject(Autenticacion);

  protected readonly historico = signal<Pagina<AlertaModelo> | null>(null);
  protected readonly cargando = signal(false);
  protected readonly pagina = signal(1);

  protected readonly comunidades = signal<Comunidad[]>([]);
  protected readonly sensores = signal<Sensor[]>([]);

  // --- Filtros ---
  protected readonly comunidadFiltro = signal<number | ''>('');
  protected readonly sensorFiltro = signal<number | ''>('');
  protected readonly fenomenoFiltro = signal<TipoFenomeno | ''>('');
  protected readonly nivelFiltro = signal<NivelAlerta | ''>('');
  protected readonly estadoFiltro = signal<EstadoAlerta | ''>('');
  protected readonly desde = signal('');
  protected readonly hasta = signal('');

  // --- Detalle ---
  protected readonly detalle = signal<AlertaModelo | null>(null);
  protected readonly comentario = signal('');
  protected readonly procesando = signal(false);

  protected readonly niveles = Object.keys(NIVELES) as NivelAlerta[];
  protected readonly estados = Object.keys(ESTADOS_ALERTA) as EstadoAlerta[];
  protected readonly fenomenos = FENOMENOS_PROTOCOLO.map((f) => ({ valor: f, ...FENOMENOS[f] }));
  protected readonly estadoMeta = ESTADOS_ALERTA;

  protected readonly sensoresFiltrados = computed(() => {
    const comunidad = this.comunidadFiltro();
    return comunidad === '' ? this.sensores() : this.sensores().filter((s) => s.comunidadId === comunidad);
  });

  async ngOnInit(): Promise<void> {
    const [comunidades, sensores] = await Promise.all([
      firstValueFrom(this.api.listarComunidades()),
      firstValueFrom(this.api.sensores()),
    ]);
    this.comunidades.set(comunidades);
    this.sensores.set(sensores);
    this.comunidadFiltro.set(this.estado.comunidadActiva()?.id ?? '');

    await this.cargar();
  }

  protected async cargar(): Promise<void> {
    this.cargando.set(true);

    try {
      this.historico.set(
        await firstValueFrom(
          this.api.alertas({
            pagina: this.pagina(),
            tamano: 15,
            comunidadId: this.comunidadFiltro() || undefined,
            sensorId: this.sensorFiltro() || undefined,
            fenomeno: this.fenomenoFiltro() || undefined,
            nivel: this.nivelFiltro() || undefined,
            estado: this.estadoFiltro() || undefined,
            desde: fechaIso(this.desde()),
            hasta: fechaIso(this.hasta()),
          }),
        ),
      );
    } finally {
      this.cargando.set(false);
    }
  }

  protected async aplicarFiltros(): Promise<void> {
    // Cambiar el filtro invalida la paginación: se vuelve siempre a la primera página.
    this.pagina.set(1);
    await this.cargar();
  }

  protected async alCambiarComunidad(): Promise<void> {
    this.sensorFiltro.set('');
    await this.aplicarFiltros();
  }

  protected async limpiar(): Promise<void> {
    this.comunidadFiltro.set('');
    this.sensorFiltro.set('');
    this.fenomenoFiltro.set('');
    this.nivelFiltro.set('');
    this.estadoFiltro.set('');
    this.desde.set('');
    this.hasta.set('');
    await this.aplicarFiltros();
  }

  protected async irA(pagina: number): Promise<void> {
    this.pagina.set(pagina);
    await this.cargar();
  }

  protected alReconocer(alerta: AlertaModelo): void {
    this.estado.marcarReconocida(alerta);
    void this.cargar();
  }

  protected alCerrar(): void {
    void this.estado.recargar();
    void this.cargar();
  }

  // ------------------------------------------------------------------- Detalle

  protected async verDetalle(alerta: AlertaModelo): Promise<void> {
    this.comentario.set('');
    // Se pide de nuevo a la API: el registro de la tabla puede haber cambiado desde que se cargó.
    this.detalle.set(await firstValueFrom(this.api.alerta(alerta.id)));
  }

  protected cerrarDetalle(): void {
    this.detalle.set(null);
  }

  protected async atender(): Promise<void> {
    const alerta = this.detalle();
    if (!alerta || this.procesando()) return;

    await this.gestionar(async () => {
      const actualizada = await firstValueFrom(
        this.api.atenderAlerta(alerta.id, this.comentario().trim() || undefined),
      );
      this.detalle.set(actualizada);
      this.estado.marcarReconocida(actualizada);
      this.notificaciones.informar('Alerta atendida', 'Quedó registrado que usted se hizo cargo.');
    });
  }

  protected async cerrarAlerta(): Promise<void> {
    const alerta = this.detalle();
    if (!alerta || this.procesando()) return;

    await this.gestionar(async () => {
      const actualizada = await firstValueFrom(
        this.api.cerrarAlerta(alerta.id, this.comentario().trim() || undefined),
      );
      this.detalle.set(actualizada);
      this.notificaciones.informar('Alerta cerrada', 'El cierre quedó registrado en la bitácora.');
      await this.estado.recargar();
    });
  }

  private async gestionar(accion: () => Promise<void>): Promise<void> {
    this.procesando.set(true);
    try {
      await accion();
      await this.cargar();
    } finally {
      this.procesando.set(false);
    }
  }

  protected fenomeno(alerta: AlertaModelo) {
    return FENOMENOS[alerta.fenomeno];
  }

  protected nivelEtiqueta(nivel: NivelAlerta): string {
    return NIVELES[nivel].etiqueta;
  }
}

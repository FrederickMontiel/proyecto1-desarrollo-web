import { DatePipe, DecimalPipe, KeyValuePipe } from '@angular/common';
import { Component, OnInit, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { Paginador } from '../../componentes/paginador/paginador';
import {
  Comunidad,
  ESTADOS_ALERTA,
  EstadisticasHistorial,
  EstadoAlerta,
  Evento,
  FENOMENOS,
  FENOMENOS_PROTOCOLO,
  NIVELES,
  NivelAlerta,
  Pagina,
  TipoFenomeno,
  fechaIso,
} from '../../core/modelos/modelos';
import { Api } from '../../core/servicios/api';
import { EstadoMonitoreo } from '../../core/servicios/estado-monitoreo';

/**
 * Historial de incidentes. Cada fila es un episodio completo —desde que se detectó el
 * riesgo hasta que las condiciones se normalizaron o alguien lo cerró—, no una lectura
 * suelta. Las estadísticas se calculan en el servidor sobre el mismo filtro que la tabla.
 */
@Component({
  selector: 'app-historial',
  imports: [DatePipe, DecimalPipe, KeyValuePipe, FormsModule, Paginador],
  templateUrl: './historial.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './historial.scss',
})
export class Historial implements OnInit {
  private readonly api = inject(Api);
  protected readonly estado = inject(EstadoMonitoreo);

  protected readonly datos = signal<Pagina<Evento> | null>(null);
  protected readonly estadisticas = signal<EstadisticasHistorial | null>(null);
  protected readonly cargando = signal(false);
  protected readonly comunidades = signal<Comunidad[]>([]);

  protected readonly pagina = signal(1);
  protected readonly comunidadFiltro = signal<number | ''>('');
  protected readonly fenomenoFiltro = signal<TipoFenomeno | ''>('');
  protected readonly nivelFiltro = signal<NivelAlerta | ''>('');
  protected readonly estadoFiltro = signal<EstadoAlerta | ''>('');
  protected readonly desde = signal('');
  protected readonly hasta = signal('');

  protected readonly fenomenos = FENOMENOS_PROTOCOLO.map((f) => ({ valor: f, ...FENOMENOS[f] }));
  protected readonly niveles = Object.keys(NIVELES) as NivelAlerta[];
  protected readonly estados = Object.keys(ESTADOS_ALERTA) as EstadoAlerta[];
  protected readonly estadoMeta = ESTADOS_ALERTA;


  async ngOnInit(): Promise<void> {
    this.comunidades.set(await firstValueFrom(this.api.listarComunidades()));
    this.comunidadFiltro.set(this.estado.comunidadActiva()?.id ?? '');
    await this.cargar();
  }

  private filtro() {
    return {
      comunidadId: this.comunidadFiltro() || undefined,
      fenomeno: this.fenomenoFiltro() || undefined,
      nivel: this.nivelFiltro() || undefined,
      estado: this.estadoFiltro() || undefined,
      desde: fechaIso(this.desde()),
      hasta: fechaIso(this.hasta()),
    };
  }

  protected async cargar(): Promise<void> {
    this.cargando.set(true);
    const filtro = this.filtro();

    try {
      const [pagina, estadisticas] = await Promise.all([
        firstValueFrom(this.api.historial({ ...filtro, pagina: this.pagina(), tamano: 20 })),
        firstValueFrom(this.api.estadisticasHistorial(filtro)),
      ]);

      this.datos.set(pagina);
      this.estadisticas.set(estadisticas);
    } finally {
      this.cargando.set(false);
    }
  }

  protected async aplicarFiltros(): Promise<void> {
    this.pagina.set(1);
    await this.cargar();
  }

  protected async limpiar(): Promise<void> {
    this.comunidadFiltro.set('');
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

  protected meta(evento: Evento) {
    return FENOMENOS[evento.fenomeno];
  }

  protected etiquetaFenomeno(clave: string): string {
    return FENOMENOS[clave as TipoFenomeno]?.etiqueta ?? clave;
  }

  /** Ancho relativo de una barra de las estadísticas, frente al mayor de su grupo. */
  protected proporcion(valor: number, grupo: Record<string, number>): number {
    const maximo = Math.max(...Object.values(grupo), 1);
    return Math.round((valor / maximo) * 100);
  }

  /** Duración legible del episodio; los que siguen abiertos se marcan como en curso. */
  protected duracion(minutosTotales: number | null): string {
    if (minutosTotales === null) return 'En curso';

    const minutos = Math.round(minutosTotales);
    if (minutos < 1) return 'menos de 1 min';
    if (minutos < 60) return `${minutos} min`;

    const horas = Math.floor(minutos / 60);
    const resto = minutos % 60;
    return resto === 0 ? `${horas} h` : `${horas} h ${resto} min`;
  }
}

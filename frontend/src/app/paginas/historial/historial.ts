import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { Evento, FENOMENOS, Pagina, TipoFenomeno } from '../../core/modelos/modelos';
import { Api } from '../../core/servicios/api';
import { EstadoMonitoreo } from '../../core/servicios/estado-monitoreo';

/**
 * Historial de incidentes. Cada fila es un episodio completo —desde que se detectó el
 * riesgo hasta que las condiciones se normalizaron—, no una lectura suelta.
 */
@Component({
  selector: 'app-historial',
  imports: [DatePipe, DecimalPipe, FormsModule],
  templateUrl: './historial.html',
  styleUrl: './historial.scss',
})
export class Historial implements OnInit {
  private readonly api = inject(Api);
  protected readonly estado = inject(EstadoMonitoreo);

  protected readonly datos = signal<Pagina<Evento> | null>(null);
  protected readonly cargando = signal(false);

  protected readonly pagina = signal(1);
  protected readonly fenomenoFiltro = signal<TipoFenomeno | ''>('');
  protected readonly desde = signal('');
  protected readonly hasta = signal('');

  /** Los cinco fenómenos del protocolo; se excluye el valor neutro del enumerado. */
  protected readonly fenomenos = (Object.keys(FENOMENOS) as TipoFenomeno[])
    .filter((f) => f !== 'Ninguno')
    .map((f) => ({ valor: f, ...FENOMENOS[f] }));

  /** Recuento por fenómeno de la página en pantalla, para el resumen superior. */
  protected readonly recuento = computed(() => {
    const elementos = this.datos()?.elementos ?? [];

    return this.fenomenos
      .map((f) => ({
        ...f,
        total: elementos.filter((e) => e.fenomeno === f.valor).length,
      }))
      .filter((f) => f.total > 0);
  });

  async ngOnInit(): Promise<void> {
    await this.cargar();
  }

  protected async cargar(): Promise<void> {
    this.cargando.set(true);

    try {
      const resultado = await firstValueFrom(
        this.api.historial({
          pagina: this.pagina(),
          tamano: 20,
          comunidadId: this.estado.comunidadActiva()?.id,
          fenomeno: this.fenomenoFiltro() || undefined,
          // El control de fecha entrega hora local; se envía en ISO para que el servidor
          // la interprete sin ambigüedad de zona horaria.
          desde: this.desde() ? new Date(this.desde()).toISOString() : undefined,
          hasta: this.hasta() ? new Date(this.hasta()).toISOString() : undefined,
        }),
      );

      this.datos.set(resultado);
    } finally {
      this.cargando.set(false);
    }
  }

  protected async aplicarFiltros(): Promise<void> {
    this.pagina.set(1);
    await this.cargar();
  }

  protected async limpiar(): Promise<void> {
    this.fenomenoFiltro.set('');
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

  /** Duración legible del episodio; los que siguen abiertos se marcan como en curso. */
  protected duracion(evento: Evento): string {
    if (evento.duracionMinutos === null) return 'En curso';

    const minutos = Math.round(evento.duracionMinutos);
    if (minutos < 1) return 'menos de 1 min';
    if (minutos < 60) return `${minutos} min`;

    const horas = Math.floor(minutos / 60);
    const resto = minutos % 60;
    return resto === 0 ? `${horas} h` : `${horas} h ${resto} min`;
  }
}

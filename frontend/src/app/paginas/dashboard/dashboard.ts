import { DatePipe, DecimalPipe, KeyValuePipe } from '@angular/common';
import { Component, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { GraficoEvolucion } from '../../componentes/grafico-evolucion/grafico-evolucion';
import { MapaComunidad } from '../../componentes/mapa-comunidad/mapa-comunidad';
import { PanelAlertas } from '../../componentes/panel-alertas/panel-alertas';
import { TarjetaSensor } from '../../componentes/tarjeta-sensor/tarjeta-sensor';
import { Alerta, FENOMENOS, NIVELES, NivelAlerta, ResumenDashboard, SENSORES, TipoFenomeno, TipoSensor } from '../../core/modelos/modelos';
import { Api } from '../../core/servicios/api';
import { Autenticacion } from '../../core/servicios/autenticacion';
import { EstadoMonitoreo } from '../../core/servicios/estado-monitoreo';
import { Notificaciones } from '../../core/servicios/notificaciones';

/**
 * Tablero principal. Reúne el estado global de la comunidad, los indicadores de cada
 * magnitud, las alertas abiertas, la evolución temporal y el croquis de la red.
 */
@Component({
  selector: 'app-dashboard',
  imports: [TarjetaSensor, PanelAlertas, GraficoEvolucion, MapaComunidad, DatePipe, DecimalPipe, KeyValuePipe],
  templateUrl: './dashboard.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  private readonly api = inject(Api);
  private readonly notificaciones = inject(Notificaciones);

  protected readonly estado = inject(EstadoMonitoreo);
  protected readonly auth = inject(Autenticacion);

  protected readonly reiniciando = signal(false);

  protected readonly nivelGlobal = computed(() => NIVELES[this.estado.nivelGlobal()]);

  /** El resumen viene filtrado por la comunidad elegida; el total de comunidades es global. */
  protected readonly totalComunidades = computed(() => this.estado.comunidades().length);

  /** Fenómenos con alerta abierta, para el encabezado de situación. */
  protected readonly fenomenosActivos = computed(() =>
    this.estado
      .alertasActivas()
      .map((a) => ({ ...FENOMENOS[a.fenomeno], nivel: a.nivel, id: a.id })),
  );

  protected readonly ultimaActualizacion = computed(() => {
    const marcas = this.estado
      .sensores()
      .map((s) => (s.ultimaLectura ? new Date(s.ultimaLectura).getTime() : 0));

    return marcas.length > 0 ? new Date(Math.max(...marcas)) : null;
  });

  /** Del más grave al menos grave, para las barras de distribución. */
  protected readonly nivelesOrdenados: NivelAlerta[] = ['Rojo', 'Naranja', 'Amarillo', 'Verde'];

  protected etiquetaNivel(nivel: NivelAlerta): string {
    return `${nivel} · ${NIVELES[nivel].etiqueta}`;
  }

  protected etiquetaFenomeno(fenomeno: string): string {
    const meta = FENOMENOS[fenomeno as TipoFenomeno];
    return meta ? `${meta.icono} ${meta.etiqueta}` : fenomeno;
  }

  protected etiquetaSensor(tipo: TipoSensor): string {
    const meta = SENSORES[tipo];
    return meta ? `${meta.icono} ${meta.etiqueta}` : tipo;
  }

  /** Ancho relativo de cada barra respecto a la mayor de su grupo. */
  protected proporcion(valor: number, grupo: Record<string, number>): number {
    const maximo = Math.max(...Object.values(grupo), 1);
    return Math.max(2, Math.round((100 * valor) / maximo));
  }

  protected serviciosCaidos(resumen: ResumenDashboard): string[] {
    const nombres = { community: 'comunidades', sensor: 'sensores', alert: 'alertas' } as const;
    return (Object.keys(nombres) as (keyof typeof nombres)[])
      .filter((clave) => resumen.disponibles && !resumen.disponibles[clave])
      .map((clave) => nombres[clave]);
  }

  protected alReconocer(alerta: Alerta): void {
    this.estado.marcarReconocida(alerta);
    this.notificaciones.informar('Alerta atendida', 'Quedó registrada en la bitácora del sistema.');
  }

  protected alCerrar(): void {
    void this.estado.recargar();
    this.notificaciones.informar('Alerta cerrada', 'El cierre quedó registrado en la bitácora.');
  }

  protected async reiniciar(): Promise<void> {
    const confirmado = confirm(
      'Reiniciar el sistema de monitoreo.\n\n' +
        'Se cerrarán todas las alertas activas, se descartarán las lecturas acumuladas y ' +
        'los sensores volverán a su valor de reposo.\n\n' +
        'El historial de eventos se conserva. ¿Desea continuar?',
    );

    if (!confirmado) return;

    this.reiniciando.set(true);

    try {
      await firstValueFrom(this.api.reiniciarSistema());
    } finally {
      this.reiniciando.set(false);
    }
  }
}

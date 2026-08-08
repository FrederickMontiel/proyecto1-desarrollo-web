import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { GraficoEvolucion } from '../../componentes/grafico-evolucion/grafico-evolucion';
import { MapaComunidad } from '../../componentes/mapa-comunidad/mapa-comunidad';
import { PanelAlertas } from '../../componentes/panel-alertas/panel-alertas';
import { TarjetaSensor } from '../../componentes/tarjeta-sensor/tarjeta-sensor';
import { Alerta, FENOMENOS, NIVELES } from '../../core/modelos/modelos';
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
  imports: [TarjetaSensor, PanelAlertas, GraficoEvolucion, MapaComunidad, DatePipe],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  private readonly api = inject(Api);
  private readonly notificaciones = inject(Notificaciones);

  protected readonly estado = inject(EstadoMonitoreo);
  protected readonly auth = inject(Autenticacion);

  protected readonly reiniciando = signal(false);

  protected readonly nivelGlobal = computed(() => NIVELES[this.estado.nivelGlobal()]);

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

  protected alReconocer(alerta: Alerta): void {
    this.estado.marcarReconocida(alerta);
    this.notificaciones.informar('Alerta reconocida', 'Quedó registrada en la bitácora del sistema.');
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

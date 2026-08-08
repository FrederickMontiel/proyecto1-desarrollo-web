import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { PanelAlertas } from '../../componentes/panel-alertas/panel-alertas';
import {
  Alerta as AlertaModelo,
  FENOMENOS,
  NIVELES,
  NivelAlerta,
  Pagina,
} from '../../core/modelos/modelos';
import { Api } from '../../core/servicios/api';
import { EstadoMonitoreo } from '../../core/servicios/estado-monitoreo';

/**
 * Bandeja de alertas: arriba las que siguen abiertas —con su acuse de recibo— y debajo
 * el registro completo, incluidas las ya cerradas, con filtro por nivel.
 */
@Component({
  selector: 'app-alertas',
  imports: [PanelAlertas, DatePipe, FormsModule],
  templateUrl: './alertas.html',
  styleUrl: './alertas.scss',
})
export class Alertas implements OnInit {
  private readonly api = inject(Api);
  protected readonly estado = inject(EstadoMonitoreo);

  protected readonly historico = signal<Pagina<AlertaModelo> | null>(null);
  protected readonly cargando = signal(false);
  protected readonly pagina = signal(1);
  protected readonly nivelFiltro = signal<NivelAlerta | ''>('');

  protected readonly niveles = Object.keys(NIVELES) as NivelAlerta[];

  async ngOnInit(): Promise<void> {
    await this.cargar();
  }

  protected async cargar(): Promise<void> {
    const comunidad = this.estado.comunidadActiva();
    this.cargando.set(true);

    try {
      const resultado = await firstValueFrom(
        this.api.alertas({
          pagina: this.pagina(),
          tamano: 15,
          comunidadId: comunidad?.id,
          nivel: this.nivelFiltro() || undefined,
        }),
      );

      this.historico.set(resultado);
    } finally {
      this.cargando.set(false);
    }
  }

  protected async cambiarFiltro(): Promise<void> {
    // Cambiar el filtro invalida la paginación: se vuelve siempre a la primera página.
    this.pagina.set(1);
    await this.cargar();
  }

  protected async irA(pagina: number): Promise<void> {
    this.pagina.set(pagina);
    await this.cargar();
  }

  protected alReconocer(alerta: AlertaModelo): void {
    this.estado.marcarReconocida(alerta);
    void this.cargar();
  }

  protected fenomeno(alerta: AlertaModelo) {
    return FENOMENOS[alerta.fenomeno];
  }

  protected nivelEtiqueta(nivel: NivelAlerta): string {
    return NIVELES[nivel].etiqueta;
  }
}

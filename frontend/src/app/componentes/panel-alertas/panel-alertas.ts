import { DatePipe } from '@angular/common';
import { Component, inject, input, output, signal, ChangeDetectionStrategy } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Alerta, FENOMENOS, NIVELES } from '../../core/modelos/modelos';
import { Api } from '../../core/servicios/api';
import { Autenticacion } from '../../core/servicios/autenticacion';

/**
 * Lista de alertas abiertas, ordenada por severidad. Cada entrada lleva el mensaje
 * descriptivo del riesgo y las acciones de atención y cierre, que son lo que el operador
 * necesita tener siempre a un clic.
 */
@Component({
  selector: 'app-panel-alertas',
  imports: [DatePipe],
  templateUrl: './panel-alertas.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './panel-alertas.scss',
})
export class PanelAlertas {
  private readonly api = inject(Api);
  protected readonly auth = inject(Autenticacion);

  readonly alertas = input.required<Alerta[]>();
  /** Se emite al atender una alerta; conserva el nombre por compatibilidad con quien la usa. */
  readonly reconocida = output<Alerta>();
  readonly cerrada = output<Alerta>();

  /** Alertas cuyo acuse está en curso, para bloquear el botón sin congelar la lista. */
  protected readonly procesando = signal<Set<number>>(new Set());

  protected fenomeno(alerta: Alerta) {
    return FENOMENOS[alerta.fenomeno];
  }

  protected nivel(alerta: Alerta) {
    return NIVELES[alerta.nivel];
  }

  protected enProceso(alerta: Alerta): boolean {
    return this.procesando().has(alerta.id);
  }

  protected async reconocer(alerta: Alerta): Promise<void> {
    await this.ejecutar(alerta, async () => {
      this.reconocida.emit(await firstValueFrom(this.api.atenderAlerta(alerta.id)));
    });
  }

  protected async cerrar(alerta: Alerta): Promise<void> {
    const confirmado = confirm(
      `Cerrar la alerta de ${this.fenomeno(alerta).etiqueta} (${this.nivel(alerta).etiqueta}).

` +
        'Quedará registrado que usted la cerró. Si la condición de riesgo persiste, el sistema ' +
        'abrirá una alerta nueva. ¿Desea continuar?',
    );
    if (!confirmado) return;

    await this.ejecutar(alerta, async () => {
      this.cerrada.emit(await firstValueFrom(this.api.cerrarAlerta(alerta.id)));
    });
  }

  private async ejecutar(alerta: Alerta, accion: () => Promise<void>): Promise<void> {
    if (this.enProceso(alerta)) return;

    this.procesando.update((s) => new Set(s).add(alerta.id));

    try {
      await accion();
    } finally {
      this.procesando.update((s) => {
        const copia = new Set(s);
        copia.delete(alerta.id);
        return copia;
      });
    }
  }
}

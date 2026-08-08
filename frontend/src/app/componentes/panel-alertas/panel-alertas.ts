import { DatePipe } from '@angular/common';
import { Component, inject, input, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Alerta, FENOMENOS, NIVELES } from '../../core/modelos/modelos';
import { Api } from '../../core/servicios/api';
import { Autenticacion } from '../../core/servicios/autenticacion';

/**
 * Lista de alertas abiertas, ordenada por severidad. Cada entrada lleva el mensaje
 * descriptivo del riesgo y el botón de acuse de recibo, que es la acción que el operador
 * necesita tener siempre a un clic.
 */
@Component({
  selector: 'app-panel-alertas',
  imports: [DatePipe],
  templateUrl: './panel-alertas.html',
  styleUrl: './panel-alertas.scss',
})
export class PanelAlertas {
  private readonly api = inject(Api);
  protected readonly auth = inject(Autenticacion);

  readonly alertas = input.required<Alerta[]>();
  readonly reconocida = output<Alerta>();

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
    if (this.enProceso(alerta)) return;

    this.procesando.update((s) => new Set(s).add(alerta.id));

    try {
      const actualizada = await firstValueFrom(this.api.reconocerAlerta(alerta.id));
      this.reconocida.emit(actualizada);
    } finally {
      this.procesando.update((s) => {
        const copia = new Set(s);
        copia.delete(alerta.id);
        return copia;
      });
    }
  }
}

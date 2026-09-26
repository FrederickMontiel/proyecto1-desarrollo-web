import { Component, input, output, ChangeDetectionStrategy } from '@angular/core';
import { Pagina } from '../../core/modelos/modelos';

/** Navegación entre páginas de un listado paginado por la API. */
@Component({
  selector: 'app-paginador',
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <nav class="paginacion" [attr.aria-label]="'Paginación de ' + unidad()">
      <button
        class="boton boton--sm"
        type="button"
        [disabled]="pagina().pagina <= 1"
        (click)="cambiar.emit(pagina().pagina - 1)">
        ← Anterior
      </button>

      <span class="texto-tenue">
        Página {{ pagina().pagina }} de {{ pagina().totalPaginas || 1 }}
        · {{ pagina().totalElementos }} {{ unidad() }}
      </span>

      <button
        class="boton boton--sm"
        type="button"
        [disabled]="pagina().pagina >= pagina().totalPaginas"
        (click)="cambiar.emit(pagina().pagina + 1)">
        Siguiente →
      </button>
    </nav>
  `,
})
export class Paginador {
  readonly pagina = input.required<Pagina<unknown>>();
  readonly unidad = input('registros');
  readonly cambiar = output<number>();
}

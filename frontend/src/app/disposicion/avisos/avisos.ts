import { Component, inject, ChangeDetectionStrategy } from '@angular/core';
import { Notificaciones } from '../../core/servicios/notificaciones';

/**
 * Pila de avisos emergentes. Se anuncia como región activa para que un lector de
 * pantalla lea las alertas nuevas sin que el usuario tenga que buscarlas.
 */
@Component({
  selector: 'app-avisos',
  template: `
    <div class="avisos" role="log" aria-live="assertive" aria-relevant="additions">
      @for (aviso of notificaciones.avisos(); track aviso.id) {
        <article class="aviso" [class]="'aviso--' + aviso.nivel.toLowerCase()">
          <div class="aviso__cuerpo">
            <strong>{{ aviso.titulo }}</strong>
            <p>{{ aviso.mensaje }}</p>
          </div>
          <button
            type="button"
            class="aviso__cerrar"
            (click)="notificaciones.descartar(aviso.id)"
            aria-label="Descartar aviso">
            ✕
          </button>
        </article>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .avisos {
      position: fixed;
      right: 1rem;
      bottom: 1rem;
      z-index: 60;
      display: flex;
      flex-direction: column;
      gap: 0.6rem;
      width: min(390px, calc(100vw - 2rem));
      pointer-events: none;
    }

    .aviso {
      pointer-events: auto;
      display: flex;
      align-items: flex-start;
      gap: 0.75rem;
      padding: 0.8rem 0.9rem;
      border-radius: var(--radio);
      border: 1px solid var(--borde);
      border-left-width: 4px;
      background: var(--superficie);
      box-shadow: var(--sombra);
      animation: aparecer 0.18s ease-out;
    }

    .aviso__cuerpo { flex: 1; }

    .aviso__cuerpo strong {
      display: block;
      font-size: 0.9rem;
      margin-bottom: 0.15rem;
    }

    .aviso__cuerpo p {
      font-size: 0.84rem;
      color: var(--texto-suave);
      line-height: 1.45;
    }

    .aviso__cerrar {
      border: none;
      background: none;
      color: var(--texto-tenue);
      cursor: pointer;
      font-size: 0.9rem;
      padding: 0.1rem 0.25rem;
      border-radius: 4px;
    }

    .aviso__cerrar:hover { color: var(--texto); background: var(--superficie-alta); }

    .aviso--info     { border-left-color: var(--primario); }
    .aviso--verde    { border-left-color: var(--verde); }
    .aviso--amarillo { border-left-color: var(--amarillo); }
    .aviso--naranja  { border-left-color: var(--naranja); }
    .aviso--rojo     { border-left-color: var(--rojo); }

    .aviso--rojo .aviso__cuerpo strong { color: var(--rojo); }
    .aviso--naranja .aviso__cuerpo strong { color: var(--naranja); }
  `,
})
export class Avisos {
  protected readonly notificaciones = inject(Notificaciones);
}

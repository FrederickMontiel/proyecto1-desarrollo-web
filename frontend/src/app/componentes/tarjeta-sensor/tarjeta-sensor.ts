import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { NIVELES, SENSORES, Sensor } from '../../core/modelos/modelos';

/**
 * Indicador principal de una magnitud. Muestra el valor vivo, su nivel de riesgo y una
 * barra que sitúa la lectura dentro del rango del instrumento, con los umbrales marcados
 * encima: de un vistazo se ve no solo cuánto mide, sino cuánto le falta para el siguiente
 * color del protocolo.
 */
@Component({
  selector: 'app-tarjeta-sensor',
  imports: [DecimalPipe, DatePipe],
  templateUrl: './tarjeta-sensor.html',
  styleUrl: './tarjeta-sensor.scss',
})
export class TarjetaSensor {
  readonly sensor = input.required<Sensor>();

  protected readonly meta = computed(() => SENSORES[this.sensor().tipo]);
  protected readonly nivel = computed(() => NIVELES[this.sensor().nivelActual]);

  protected readonly inactivo = computed(() => this.sensor().estado !== 'Activo');

  /**
   * Un sensor apagado lo desactivó alguien; uno sin señal está averiado. Mostrar el
   * segundo como si todo fuera bien, con su última lectura congelada, daría una falsa
   * sensación de normalidad justo donde el sistema ya no sabe lo que ocurre.
   */
  protected readonly sinSenal = computed(() => this.sensor().estado === 'SinSenal');

  /** Posición de la lectura dentro del rango del sensor, en porcentaje. */
  protected readonly porcentaje = computed(() => this.aPorcentaje(this.sensor().valorActual));

  /** Marcas de umbral que caen dentro del rango, listas para dibujar sobre la barra. */
  protected readonly marcas = computed(() => {
    const s = this.sensor();

    const candidatos: { valor: number | null; clase: string }[] = [
      { valor: s.umbralAmarilloAlto, clase: 'amarillo' },
      { valor: s.umbralNaranjaAlto, clase: 'naranja' },
      { valor: s.umbralRojoAlto, clase: 'rojo' },
      { valor: s.umbralAmarilloBajo, clase: 'amarillo' },
      { valor: s.umbralNaranjaBajo, clase: 'naranja' },
      { valor: s.umbralRojoBajo, clase: 'rojo' },
    ];

    return candidatos
      .filter((c): c is { valor: number; clase: string } => c.valor !== null)
      .map((c) => ({ clase: c.clase, posicion: this.aPorcentaje(c.valor) }));
  });

  private aPorcentaje(valor: number): number {
    const s = this.sensor();
    const rango = s.valorMaximo - s.valorMinimo;

    if (rango <= 0) return 0;

    return Math.min(100, Math.max(0, ((valor - s.valorMinimo) / rango) * 100));
  }
}

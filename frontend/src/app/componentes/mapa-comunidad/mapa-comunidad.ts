import { Component, computed, input } from '@angular/core';
import { Comunidad, NIVELES, SENSORES, Sensor } from '../../core/modelos/modelos';

interface Marcador {
  sensor: Sensor;
  x: number;
  y: number;
  icono: string;
  color: string;
  activo: boolean;
}

/**
 * Croquis de la comunidad con la posición y el estado de cada sensor.
 *
 * Se dibuja como SVG propio en lugar de usar un mapa con teselas remotas: el sistema
 * está pensado para zonas rurales con conexión intermitente, y una vista que depende de
 * un servidor de teselas se queda en blanco justo cuando más se necesita. Las
 * coordenadas reales se proyectan sobre el lienzo, así que las posiciones relativas
 * entre estaciones son correctas.
 */
@Component({
  selector: 'app-mapa-comunidad',
  templateUrl: './mapa-comunidad.html',
  styleUrl: './mapa-comunidad.scss',
})
export class MapaComunidad {
  readonly comunidad = input.required<Comunidad | null>();
  readonly sensores = input.required<Sensor[]>();

  /** Margen interior del lienzo para que ningún marcador quede pegado al borde. */
  private readonly margen = 14;

  protected readonly marcadores = computed<Marcador[]>(() => {
    const sensores = this.sensores();
    if (sensores.length === 0) return [];

    const latitudes = sensores.map((s) => s.latitud);
    const longitudes = sensores.map((s) => s.longitud);

    const latMin = Math.min(...latitudes);
    const latMax = Math.max(...latitudes);
    const lonMin = Math.min(...longitudes);
    const lonMax = Math.max(...longitudes);

    return sensores.map((sensor) => ({
      sensor,
      // La longitud crece hacia el este y la latitud hacia el norte, que en pantalla es
      // hacia arriba: de ahí que el eje Y se invierta.
      x: this.proyectar(sensor.longitud, lonMin, lonMax),
      y: 100 - this.proyectar(sensor.latitud, latMin, latMax),
      icono: SENSORES[sensor.tipo].icono,
      color: NIVELES[sensor.nivelActual].color,
      activo: sensor.estado === 'Activo',
    }));
  });

  protected readonly leyenda = Object.entries(NIVELES).map(([nivel, meta]) => ({
    nivel,
    etiqueta: meta.etiqueta,
    color: meta.color,
  }));

  private proyectar(valor: number, minimo: number, maximo: number): number {
    // Con un solo sensor, o con todos en el mismo punto, el rango es cero: se centra.
    if (maximo - minimo < 1e-9) return 50;

    const util = 100 - this.margen * 2;
    return this.margen + ((valor - minimo) / (maximo - minimo)) * util;
  }
}

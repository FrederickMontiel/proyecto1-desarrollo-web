import {
  AfterViewInit,
  Component,
  ElementRef,
  OnDestroy,
  computed,
  effect,
  input,
  signal,
  viewChild,
  ChangeDetectionStrategy
} from '@angular/core';
import {
  CategoryScale,
  Chart,
  ChartDataset,
  Filler,
  Legend,
  LineController,
  LineElement,
  LinearScale,
  PointElement,
  Tooltip,
} from 'chart.js';
import { PuntoSerie, SENSORES, Sensor, TipoSensor } from '../../core/modelos/modelos';

// Registro selectivo en vez de `registerables`: solo entra en el paquete lo que este
// gráfico usa realmente.
Chart.register(
  LineController,
  LineElement,
  PointElement,
  LinearScale,
  CategoryScale,
  Tooltip,
  Legend,
  Filler,
);

/**
 * Evolución temporal de una magnitud. Se grafica una sola a la vez y con su unidad real:
 * superponer °C, mm y km/h en un mismo eje daría una imagen vistosa pero ilegible.
 * Sobre la curva se trazan los umbrales de alerta del sensor, que es lo que permite leer
 * la tendencia en términos de riesgo y no solo de número.
 */
@Component({
  selector: 'app-grafico-evolucion',
  templateUrl: './grafico-evolucion.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './grafico-evolucion.scss',
})
export class GraficoEvolucion implements AfterViewInit, OnDestroy {
  readonly sensores = input.required<Sensor[]>();
  readonly series = input.required<Map<number, PuntoSerie[]>>();

  private readonly lienzo = viewChild.required<ElementRef<HTMLCanvasElement>>('lienzo');
  private grafico: Chart | null = null;

  protected readonly tipoSeleccionado = signal<TipoSensor>('Temperatura');

  /** Magnitudes que la comunidad mide realmente; no se ofrecen pestañas vacías. */
  protected readonly tiposDisponibles = computed(() => {
    const vistos = new Set<TipoSensor>();
    return this.sensores()
      .filter((s) => (vistos.has(s.tipo) ? false : vistos.add(s.tipo)))
      .map((s) => ({ tipo: s.tipo, etiqueta: SENSORES[s.tipo].etiqueta, icono: SENSORES[s.tipo].icono }));
  });

  protected readonly sensorActivo = computed(() =>
    this.sensores().find((s) => s.tipo === this.tipoSeleccionado()),
  );

  constructor() {
    // Un único efecto redibuja ante cualquier cambio: nuevas muestras, cambio de
    // magnitud o alta y baja de sensores.
    effect(() => {
      const sensor = this.sensorActivo();
      const puntos = sensor ? (this.series().get(sensor.id) ?? []) : [];
      this.actualizar(sensor, puntos);
    });
  }

  ngAfterViewInit(): void {
    this.crear();
    this.actualizar(this.sensorActivo(), this.puntosActuales());
  }

  ngOnDestroy(): void {
    this.grafico?.destroy();
    this.grafico = null;
  }

  protected seleccionar(tipo: TipoSensor): void {
    this.tipoSeleccionado.set(tipo);
  }

  private puntosActuales(): PuntoSerie[] {
    const sensor = this.sensorActivo();
    return sensor ? (this.series().get(sensor.id) ?? []) : [];
  }

  private crear(): void {
    const contexto = this.lienzo().nativeElement.getContext('2d');
    if (!contexto) return;

    this.grafico = new Chart(contexto, {
      type: 'line',
      data: { labels: [], datasets: [] },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        // Sin animación al añadir muestras: con una lectura cada pocos segundos, la
        // transición haría que la curva pareciera moverse sola todo el tiempo.
        animation: false,
        interaction: { mode: 'index', intersect: false },
        plugins: {
          legend: {
            display: true,
            position: 'top',
            align: 'end',
            labels: {
              boxWidth: 12,
              boxHeight: 2,
              usePointStyle: false,
              color: this.color('--texto-suave'),
              font: { size: 11 },
              filter: (item) => !item.text.startsWith('__'),
            },
          },
          tooltip: {
            backgroundColor: this.color('--superficie-alta'),
            titleColor: this.color('--texto'),
            bodyColor: this.color('--texto-suave'),
            borderColor: this.color('--borde'),
            borderWidth: 1,
            padding: 10,
          },
        },
        scales: {
          x: {
            ticks: {
              color: this.color('--texto-tenue'),
              font: { size: 10 },
              maxRotation: 0,
              autoSkipPadding: 24,
            },
            grid: { color: this.color('--borde-suave'), drawTicks: false },
          },
          y: {
            ticks: { color: this.color('--texto-tenue'), font: { size: 10 } },
            grid: { color: this.color('--borde-suave'), drawTicks: false },
          },
        },
      },
    });
  }

  private actualizar(sensor: Sensor | undefined, puntos: PuntoSerie[]): void {
    if (!this.grafico || !sensor) return;

    const etiquetas = puntos.map((p) =>
      new Date(p.fechaHora).toLocaleTimeString('es', {
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
      }),
    );

    const datasets: ChartDataset<'line'>[] = [
      {
        label: `${SENSORES[sensor.tipo].etiqueta} (${sensor.unidadMedida})`,
        data: puntos.map((p) => p.valor),
        borderColor: this.color('--primario'),
        backgroundColor: 'rgba(56, 189, 248, 0.12)',
        borderWidth: 2,
        pointRadius: 0,
        pointHoverRadius: 4,
        tension: 0.3,
        fill: true,
      },
    ];

    // Los umbrales se dibujan como rectas constantes: dan la referencia de riesgo sin
    // necesidad de un plugin de anotaciones adicional.
    const umbrales: { valor: number | null; etiqueta: string; variable: string }[] = [
      { valor: sensor.umbralAmarilloAlto, etiqueta: 'Precaución', variable: '--amarillo' },
      { valor: sensor.umbralNaranjaAlto, etiqueta: 'Alerta', variable: '--naranja' },
      { valor: sensor.umbralRojoAlto, etiqueta: 'Emergencia', variable: '--rojo' },
      { valor: sensor.umbralAmarilloBajo, etiqueta: 'Precaución (bajo)', variable: '--amarillo' },
      { valor: sensor.umbralNaranjaBajo, etiqueta: 'Alerta (bajo)', variable: '--naranja' },
      { valor: sensor.umbralRojoBajo, etiqueta: 'Emergencia (bajo)', variable: '--rojo' },
    ];

    for (const umbral of umbrales) {
      if (umbral.valor === null) continue;

      datasets.push({
        label: umbral.etiqueta,
        data: etiquetas.map(() => umbral.valor as number),
        borderColor: this.color(umbral.variable),
        borderWidth: 1,
        borderDash: [5, 4],
        pointRadius: 0,
        pointHoverRadius: 0,
        fill: false,
      });
    }

    this.grafico.data.labels = etiquetas;
    this.grafico.data.datasets = datasets;
    this.grafico.update('none');
  }

  /** Lee un color del tema activo para que el gráfico siga el modo claro u oscuro. */
  private color(variable: string): string {
    return getComputedStyle(document.documentElement).getPropertyValue(variable).trim() || '#888';
  }
}

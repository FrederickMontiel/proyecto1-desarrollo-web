import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { Paginador } from '../../componentes/paginador/paginador';
import {
  Comunidad,
  LecturaHistorica,
  ORDEN_TIPOS_SENSOR,
  Pagina,
  SENSORES,
  Sensor,
  TipoSensor,
  fechaIso,
} from '../../core/modelos/modelos';
import { Api } from '../../core/servicios/api';
import { Autenticacion } from '../../core/servicios/autenticacion';
import { EstadoMonitoreo } from '../../core/servicios/estado-monitoreo';
import { Notificaciones } from '../../core/servicios/notificaciones';

/**
 * Consulta histórica de lecturas por comunidad, sensor, tipo y rango de fechas. El
 * operador puede registrar una lectura manual —que se evalúa contra las reglas igual que
 * una automática— y el administrador puede eliminar lecturas erróneas.
 */
@Component({
  selector: 'app-lecturas',
  imports: [FormsModule, DatePipe, DecimalPipe, Paginador],
  changeDetection: ChangeDetectionStrategy.Eager,
  templateUrl: './lecturas.html',
})
export class Lecturas implements OnInit {
  private readonly api = inject(Api);
  private readonly notificaciones = inject(Notificaciones);
  private readonly estado = inject(EstadoMonitoreo);

  protected readonly auth = inject(Autenticacion);

  protected readonly datos = signal<Pagina<LecturaHistorica> | null>(null);
  protected readonly cargando = signal(false);
  protected readonly pagina = signal(1);

  protected readonly comunidades = signal<Comunidad[]>([]);
  protected readonly sensores = signal<Sensor[]>([]);

  // --- Filtros ---
  protected readonly comunidadFiltro = signal<number | ''>('');
  protected readonly sensorFiltro = signal<number | ''>('');
  protected readonly tipoFiltro = signal<TipoSensor | ''>('');
  protected readonly desde = signal('');
  protected readonly hasta = signal('');

  // --- Registro manual ---
  protected readonly sensorManual = signal<number | ''>('');
  protected readonly valorManual = signal<number | null>(null);
  protected readonly registrando = signal(false);

  protected readonly tipos = ORDEN_TIPOS_SENSOR.map((t) => ({ valor: t, ...SENSORES[t] }));
  protected readonly meta = SENSORES;

  /** Sensores del filtro de comunidad; sin comunidad elegida se ofrecen todos. */
  protected readonly sensoresFiltrados = computed(() => {
    const comunidad = this.comunidadFiltro();
    return comunidad === '' ? this.sensores() : this.sensores().filter((s) => s.comunidadId === comunidad);
  });

  /** Solo admiten lecturas los sensores activos o sin señal. */
  protected readonly sensoresRegistrables = computed(() =>
    this.sensoresFiltrados().filter((s) => s.estado !== 'Inactivo'),
  );

  protected readonly sensorElegido = computed(() =>
    this.sensores().find((s) => s.id === this.sensorManual()),
  );

  async ngOnInit(): Promise<void> {
    const [comunidades, sensores] = await Promise.all([
      firstValueFrom(this.api.listarComunidades()),
      firstValueFrom(this.api.sensores()),
    ]);

    this.comunidades.set(comunidades);
    this.sensores.set(sensores);
    this.comunidadFiltro.set(this.estado.comunidadActiva()?.id ?? '');

    await this.cargar();
  }

  protected async cargar(): Promise<void> {
    this.cargando.set(true);

    try {
      this.datos.set(
        await firstValueFrom(
          this.api.lecturas({
            pagina: this.pagina(),
            tamano: 50,
            comunidadId: this.comunidadFiltro() || undefined,
            sensorId: this.sensorFiltro() || undefined,
            tipo: this.tipoFiltro() || undefined,
            desde: fechaIso(this.desde()),
            hasta: fechaIso(this.hasta()),
          }),
        ),
      );
    } finally {
      this.cargando.set(false);
    }
  }

  protected async aplicarFiltros(): Promise<void> {
    this.pagina.set(1);
    await this.cargar();
  }

  protected async alCambiarComunidad(): Promise<void> {
    // El sensor elegido puede no pertenecer a la comunidad nueva.
    this.sensorFiltro.set('');
    this.sensorManual.set('');
    await this.aplicarFiltros();
  }

  protected async limpiar(): Promise<void> {
    this.comunidadFiltro.set('');
    this.sensorFiltro.set('');
    this.tipoFiltro.set('');
    this.desde.set('');
    this.hasta.set('');
    await this.aplicarFiltros();
  }

  protected async irA(pagina: number): Promise<void> {
    this.pagina.set(pagina);
    await this.cargar();
  }

  protected async registrar(): Promise<void> {
    const sensorId = this.sensorManual();
    const valor = this.valorManual();
    if (sensorId === '' || valor === null || this.registrando()) return;

    this.registrando.set(true);

    try {
      const lectura = await firstValueFrom(this.api.registrarLectura(sensorId, valor));
      this.notificaciones.informar(
        'Lectura registrada',
        `${lectura.sensorCodigo}: ${lectura.valor} ${lectura.unidadMedida}. Se evaluó contra las reglas.`,
      );
      this.valorManual.set(null);
      await this.aplicarFiltros();
    } finally {
      this.registrando.set(false);
    }
  }

  protected async eliminar(lectura: LecturaHistorica): Promise<void> {
    const confirmado = confirm(
      `Eliminar la lectura de ${lectura.sensorCodigo} (${lectura.valor} ${lectura.unidadMedida}).\n\n` +
        'La eliminación queda registrada en la bitácora. ¿Desea continuar?',
    );
    if (!confirmado) return;

    await firstValueFrom(this.api.eliminarLectura(lectura.id));
    await this.cargar();
  }
}

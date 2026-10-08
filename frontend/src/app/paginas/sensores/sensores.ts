import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import {
  Comunidad,
  EstadoSensor,
  ORDEN_TIPOS_SENSOR,
  PlantillaSensor,
  SENSORES,
  Sensor,
  TipoSensor,
} from '../../core/modelos/modelos';
import { Api } from '../../core/servicios/api';
import { Autenticacion } from '../../core/servicios/autenticacion';
import { EstadoMonitoreo } from '../../core/servicios/estado-monitoreo';
import { Notificaciones } from '../../core/servicios/notificaciones';

/**
 * Administración de la red de sensores: alta, edición de umbrales, activación y baja,
 * además de la inyección manual de lecturas para ensayar escenarios de alerta. El listado
 * se filtra por comunidad, tipo, estado y código.
 */
@Component({
  selector: 'app-sensores',
  imports: [ReactiveFormsModule, FormsModule, DecimalPipe, DatePipe],
  templateUrl: './sensores.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './sensores.scss',
})
export class Sensores implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(Api);
  private readonly notificaciones = inject(Notificaciones);

  protected readonly estado = inject(EstadoMonitoreo);
  protected readonly auth = inject(Autenticacion);

  protected readonly plantillas = signal<PlantillaSensor[]>([]);
  protected readonly comunidades = signal<Comunidad[]>([]);
  protected readonly sensores = signal<Sensor[]>([]);
  protected readonly cargando = signal(false);
  protected readonly guardando = signal(false);
  protected readonly formularioAbierto = signal(false);
  protected readonly editando = signal<Sensor | null>(null);

  // --- Filtros ---
  protected readonly comunidadFiltro = signal<number | ''>('');
  protected readonly tipoFiltro = signal<TipoSensor | ''>('');
  protected readonly estadoFiltro = signal<EstadoSensor | ''>('');
  protected readonly codigoFiltro = signal('');

  protected readonly meta = SENSORES;

  protected readonly tipos = ORDEN_TIPOS_SENSOR.map((tipo, indice) => ({
    tipo,
    valor: indice + 1,
    ...SENSORES[tipo],
  }));

  protected readonly activos = computed(() => this.sensores().filter((s) => s.estado === 'Activo').length);

  protected readonly formulario = this.fb.nonNullable.group({
    comunidadId: [0, [Validators.required, Validators.min(1)]],
    codigo: ['', [Validators.required, Validators.maxLength(30)]],
    nombre: ['', [Validators.required, Validators.maxLength(120)]],
    tipo: [1, Validators.required],
    unidadMedida: [''],
    ubicacion: ['', Validators.maxLength(200)],
    descripcion: ['', Validators.maxLength(500)],
    fechaInstalacion: [this.hoy()],
    estado: ['Activo' as EstadoSensor],
    valorMinimo: [0],
    valorMaximo: [100],
    variacionMaxima: [1],
    valorInicial: [0],
    umbralAmarilloAlto: [null as number | null],
    umbralNaranjaAlto: [null as number | null],
    umbralRojoAlto: [null as number | null],
    umbralAmarilloBajo: [null as number | null],
    umbralNaranjaBajo: [null as number | null],
    umbralRojoBajo: [null as number | null],
  });

  async ngOnInit(): Promise<void> {
    const [plantillas, comunidades] = await Promise.all([
      firstValueFrom(this.api.plantillasSensor()),
      firstValueFrom(this.api.listarComunidades()),
    ]);

    this.plantillas.set(plantillas);
    this.comunidades.set(comunidades);
    this.comunidadFiltro.set(this.estado.comunidadActiva()?.id ?? '');

    await this.cargar();
  }

  protected async cargar(): Promise<void> {
    this.cargando.set(true);

    try {
      this.sensores.set(
        await firstValueFrom(
          this.api.sensores({
            comunidadId: this.comunidadFiltro() || undefined,
            tipo: this.tipoFiltro() || undefined,
            estado: this.estadoFiltro() || undefined,
            codigo: this.codigoFiltro().trim() || undefined,
          }),
        ),
      );
    } finally {
      this.cargando.set(false);
    }
  }

  protected async limpiar(): Promise<void> {
    this.comunidadFiltro.set('');
    this.tipoFiltro.set('');
    this.estadoFiltro.set('');
    this.codigoFiltro.set('');
    await this.cargar();
  }

  /** Tras un cambio se refrescan el listado y el tablero, que muestra la misma red. */
  private async refrescar(): Promise<void> {
    await Promise.all([this.cargar(), this.estado.recargar()]);
  }

  // ------------------------------------------------------------------ Formulario

  protected abrirAlta(): void {
    this.editando.set(null);
    this.formulario.reset({
      comunidadId: this.comunidadFiltro() || this.estado.comunidadActiva()?.id || 0,
      tipo: 1,
      codigo: '',
      nombre: '',
      ubicacion: '',
      descripcion: '',
      fechaInstalacion: this.hoy(),
      estado: 'Activo',
    });
    this.formulario.controls.codigo.enable();
    this.formulario.controls.tipo.enable();
    this.formulario.controls.estado.enable();
    this.aplicarPlantilla(1);
    this.formularioAbierto.set(true);
  }

  protected abrirEdicion(sensor: Sensor): void {
    this.editando.set(sensor);

    this.formulario.setValue({
      comunidadId: sensor.comunidadId,
      codigo: sensor.codigo,
      nombre: sensor.nombre,
      tipo: ORDEN_TIPOS_SENSOR.indexOf(sensor.tipo) + 1,
      unidadMedida: sensor.unidadMedida,
      ubicacion: sensor.ubicacion ?? '',
      descripcion: sensor.descripcion ?? '',
      fechaInstalacion: sensor.fechaInstalacion?.slice(0, 10) ?? '',
      estado: sensor.estado,
      valorMinimo: sensor.valorMinimo,
      valorMaximo: sensor.valorMaximo,
      variacionMaxima: sensor.variacionMaxima,
      valorInicial: sensor.valorActual,
      umbralAmarilloAlto: sensor.umbralAmarilloAlto,
      umbralNaranjaAlto: sensor.umbralNaranjaAlto,
      umbralRojoAlto: sensor.umbralRojoAlto,
      umbralAmarilloBajo: sensor.umbralAmarilloBajo,
      umbralNaranjaBajo: sensor.umbralNaranjaBajo,
      umbralRojoBajo: sensor.umbralRojoBajo,
    });

    // El código identifica al sensor en el inventario; cambiarlo rompería la trazabilidad
    // de las alertas ya emitidas, así que solo se fija en el alta. Lo mismo vale para el
    // tipo, y el estado tiene su propia acción en el listado.
    this.formulario.controls.codigo.disable();
    this.formulario.controls.tipo.disable();
    this.formulario.controls.estado.disable();
    this.formularioAbierto.set(true);
  }

  protected cerrar(): void {
    this.formularioAbierto.set(false);
    this.editando.set(null);
  }

  /** Al elegir el tipo se cargan la unidad, el rango y los umbrales recomendados. */
  protected alCambiarTipo(evento: Event): void {
    this.aplicarPlantilla(Number((evento.target as HTMLSelectElement).value));
  }

  private aplicarPlantilla(tipoValor: number): void {
    const plantilla = this.plantillas().find((p) => p.tipoValor === tipoValor);
    if (!plantilla) return;

    this.formulario.patchValue({
      unidadMedida: plantilla.unidad,
      valorMinimo: plantilla.valorMinimo,
      valorMaximo: plantilla.valorMaximo,
      variacionMaxima: plantilla.variacionMaxima,
      valorInicial: plantilla.valorReposo,
      umbralAmarilloAlto: plantilla.umbralAmarilloAlto,
      umbralNaranjaAlto: plantilla.umbralNaranjaAlto,
      umbralRojoAlto: plantilla.umbralRojoAlto,
      umbralAmarilloBajo: plantilla.umbralAmarilloBajo,
      umbralNaranjaBajo: plantilla.umbralNaranjaBajo,
      umbralRojoBajo: plantilla.umbralRojoBajo,
    });
  }

  protected async guardar(): Promise<void> {
    if (this.formulario.invalid || this.guardando()) {
      this.formulario.markAllAsTouched();
      return;
    }

    this.guardando.set(true);
    const datos = this.formulario.getRawValue();

    const comunes = {
      comunidadId: Number(datos.comunidadId),
      nombre: datos.nombre,
      unidadMedida: datos.unidadMedida,
      ubicacion: datos.ubicacion.trim() || null,
      descripcion: datos.descripcion.trim() || null,
      fechaInstalacion: datos.fechaInstalacion || null,
      valorMinimo: datos.valorMinimo,
      valorMaximo: datos.valorMaximo,
      variacionMaxima: datos.variacionMaxima,
      umbralAmarilloAlto: datos.umbralAmarilloAlto,
      umbralNaranjaAlto: datos.umbralNaranjaAlto,
      umbralRojoAlto: datos.umbralRojoAlto,
      umbralAmarilloBajo: datos.umbralAmarilloBajo,
      umbralNaranjaBajo: datos.umbralNaranjaBajo,
      umbralRojoBajo: datos.umbralRojoBajo,
    };

    try {
      const enEdicion = this.editando();

      if (enEdicion) {
        await firstValueFrom(this.api.actualizarSensor(enEdicion.id, comunes));
        this.notificaciones.informar('Sensor actualizado', `${datos.codigo} quedó recalibrado.`);
      } else {
        await firstValueFrom(
          this.api.crearSensor({
            ...comunes,
            codigo: datos.codigo,
            tipo: Number(datos.tipo),
            estado: datos.estado,
            valorInicial: datos.valorInicial,
          }),
        );

        this.notificaciones.informar('Sensor agregado', `${datos.codigo} ya forma parte de la red.`);
      }

      await this.refrescar();
      this.cerrar();
    } finally {
      this.guardando.set(false);
    }
  }

  // ---------------------------------------------------------------- Operaciones

  protected async alternarEstado(sensor: Sensor): Promise<void> {
    const nuevo: EstadoSensor = sensor.estado === 'Inactivo' ? 'Activo' : 'Inactivo';

    await firstValueFrom(this.api.cambiarEstadoSensor(sensor.id, nuevo));
    await this.refrescar();
  }

  protected async establecerValor(sensor: Sensor): Promise<void> {
    const entrada = prompt(
      `Valor manual para ${sensor.codigo} (${sensor.valorMinimo} a ${sensor.valorMaximo} ${sensor.unidadMedida}):`,
      String(sensor.valorActual),
    );

    if (entrada === null) return;

    const valor = Number(entrada.replace(',', '.'));
    if (Number.isNaN(valor)) {
      this.notificaciones.error('El valor ingresado no es un número válido.');
      return;
    }

    await firstValueFrom(this.api.establecerValorSensor(sensor.id, valor));
    await this.refrescar();
  }

  protected async eliminar(sensor: Sensor): Promise<void> {
    const confirmado = confirm(
      `Eliminar el sensor ${sensor.codigo}.\n\n` +
        'Se borrarán sus lecturas históricas. Las alertas que generó se conservan en el ' +
        'historial. ¿Desea continuar?',
    );

    if (!confirmado) return;

    await firstValueFrom(this.api.eliminarSensor(sensor.id));
    await this.refrescar();
    this.notificaciones.informar('Sensor eliminado', `${sensor.codigo} salió de la red.`);
  }

  private hoy(): string {
    return new Date().toISOString().slice(0, 10);
  }
}

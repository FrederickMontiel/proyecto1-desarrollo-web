import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import {
  EstadoSensor,
  PlantillaSensor,
  SENSORES,
  Sensor,
  TipoSensor,
} from '../../core/modelos/modelos';
import { Api } from '../../core/servicios/api';
import { Autenticacion } from '../../core/servicios/autenticacion';
import { EstadoMonitoreo } from '../../core/servicios/estado-monitoreo';
import { Notificaciones } from '../../core/servicios/notificaciones';

/** Orden fijo de los tipos, tal como los numera el enumerado del backend. */
const ORDEN_TIPOS: TipoSensor[] = ['Temperatura', 'Humedad', 'Viento', 'Lluvia', 'NivelRio'];

/**
 * Administración de la red de sensores: alta, edición de umbrales, activación y baja,
 * además de la inyección manual de lecturas para ensayar escenarios de alerta.
 */
@Component({
  selector: 'app-sensores',
  imports: [ReactiveFormsModule, DecimalPipe, DatePipe],
  templateUrl: './sensores.html',
  styleUrl: './sensores.scss',
})
export class Sensores implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(Api);
  private readonly notificaciones = inject(Notificaciones);

  protected readonly estado = inject(EstadoMonitoreo);
  protected readonly auth = inject(Autenticacion);

  protected readonly plantillas = signal<PlantillaSensor[]>([]);
  protected readonly guardando = signal(false);
  protected readonly formularioAbierto = signal(false);
  protected readonly editando = signal<Sensor | null>(null);

  protected readonly meta = SENSORES;

  protected readonly tipos = computed(() =>
    ORDEN_TIPOS.map((tipo, indice) => ({ tipo, valor: indice + 1, ...SENSORES[tipo] })),
  );

  protected readonly formulario = this.fb.nonNullable.group({
    codigo: ['', [Validators.required, Validators.maxLength(30)]],
    nombre: ['', [Validators.required, Validators.maxLength(120)]],
    tipo: [1, Validators.required],
    unidadMedida: [''],
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
    this.plantillas.set(await firstValueFrom(this.api.plantillasSensor()));
  }

  // ------------------------------------------------------------------ Formulario

  protected abrirAlta(): void {
    this.editando.set(null);
    this.formulario.reset({ tipo: 1, codigo: '', nombre: '' });
    this.formulario.controls.codigo.enable();
    this.aplicarPlantilla(1);
    this.formularioAbierto.set(true);
  }

  protected abrirEdicion(sensor: Sensor): void {
    this.editando.set(sensor);

    this.formulario.setValue({
      codigo: sensor.codigo,
      nombre: sensor.nombre,
      tipo: ORDEN_TIPOS.indexOf(sensor.tipo) + 1,
      unidadMedida: sensor.unidadMedida,
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
    // de las alertas ya emitidas, así que solo se fija en el alta.
    this.formulario.controls.codigo.disable();
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
    const comunidad = this.estado.comunidadActiva();
    if (this.formulario.invalid || !comunidad || this.guardando()) {
      this.formulario.markAllAsTouched();
      return;
    }

    this.guardando.set(true);
    const datos = this.formulario.getRawValue();

    try {
      const enEdicion = this.editando();

      if (enEdicion) {
        await firstValueFrom(
          this.api.actualizarSensor(enEdicion.id, {
            nombre: datos.nombre,
            unidadMedida: datos.unidadMedida,
            valorMinimo: datos.valorMinimo,
            valorMaximo: datos.valorMaximo,
            variacionMaxima: datos.variacionMaxima,
            umbralAmarilloAlto: datos.umbralAmarilloAlto,
            umbralNaranjaAlto: datos.umbralNaranjaAlto,
            umbralRojoAlto: datos.umbralRojoAlto,
            umbralAmarilloBajo: datos.umbralAmarilloBajo,
            umbralNaranjaBajo: datos.umbralNaranjaBajo,
            umbralRojoBajo: datos.umbralRojoBajo,
          }),
        );

        this.notificaciones.informar('Sensor actualizado', `${datos.codigo} quedó recalibrado.`);
      } else {
        await firstValueFrom(
          this.api.crearSensor({
            comunidadId: comunidad.id,
            codigo: datos.codigo,
            nombre: datos.nombre,
            tipo: datos.tipo,
            unidadMedida: datos.unidadMedida,
            valorMinimo: datos.valorMinimo,
            valorMaximo: datos.valorMaximo,
            variacionMaxima: datos.variacionMaxima,
            valorInicial: datos.valorInicial,
            umbralAmarilloAlto: datos.umbralAmarilloAlto,
            umbralNaranjaAlto: datos.umbralNaranjaAlto,
            umbralRojoAlto: datos.umbralRojoAlto,
            umbralAmarilloBajo: datos.umbralAmarilloBajo,
            umbralNaranjaBajo: datos.umbralNaranjaBajo,
            umbralRojoBajo: datos.umbralRojoBajo,
          }),
        );

        this.notificaciones.informar('Sensor agregado', `${datos.codigo} ya forma parte de la red.`);
      }

      await this.estado.recargar();
      this.cerrar();
    } finally {
      this.guardando.set(false);
    }
  }

  // ---------------------------------------------------------------- Operaciones

  protected async alternarEstado(sensor: Sensor): Promise<void> {
    const nuevo: EstadoSensor = sensor.estado === 'Activo' ? 'Inactivo' : 'Activo';

    await firstValueFrom(this.api.cambiarEstadoSensor(sensor.id, nuevo));
    await this.estado.recargar();
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
    await this.estado.recargar();
  }

  protected async eliminar(sensor: Sensor): Promise<void> {
    const confirmado = confirm(
      `Eliminar el sensor ${sensor.codigo}.\n\n` +
        'Se borrarán sus lecturas históricas. Las alertas que generó se conservan en el ' +
        'historial. ¿Desea continuar?',
    );

    if (!confirmado) return;

    await firstValueFrom(this.api.eliminarSensor(sensor.id));
    await this.estado.recargar();
    this.notificaciones.informar('Sensor eliminado', `${sensor.codigo} salió de la red.`);
  }
}

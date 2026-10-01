import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { Comunidad } from '../../core/modelos/modelos';
import { Api } from '../../core/servicios/api';
import { Autenticacion } from '../../core/servicios/autenticacion';
import { EstadoMonitoreo } from '../../core/servicios/estado-monitoreo';
import { Notificaciones } from '../../core/servicios/notificaciones';

/**
 * Administración de comunidades. Cualquier usuario puede consultar el listado; dar de alta,
 * editar o desactivar queda reservado al administrador, tanto aquí como en la API.
 */
@Component({
  selector: 'app-comunidades',
  imports: [ReactiveFormsModule, FormsModule, DatePipe, DecimalPipe],
  changeDetection: ChangeDetectionStrategy.Eager,
  templateUrl: './comunidades.html',
})
export class Comunidades implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(Api);
  private readonly notificaciones = inject(Notificaciones);
  private readonly estado = inject(EstadoMonitoreo);

  protected readonly auth = inject(Autenticacion);

  protected readonly comunidades = signal<Comunidad[]>([]);
  protected readonly cargando = signal(false);
  protected readonly guardando = signal(false);
  protected readonly formularioAbierto = signal(false);
  protected readonly editando = signal<Comunidad | null>(null);

  // --- Filtros ---
  protected readonly busqueda = signal('');
  protected readonly estadoFiltro = signal<'' | 'true' | 'false'>('');
  protected readonly municipioFiltro = signal('');
  protected readonly departamentoFiltro = signal('');

  /** Catálogos para los desplegables, tomados de todas las comunidades conocidas. */
  private readonly todas = signal<Comunidad[]>([]);
  protected readonly municipios = computed(() => this.unicos(this.todas().map((c) => c.municipio)));
  protected readonly departamentos = computed(() =>
    this.unicos(this.todas().map((c) => c.departamento)),
  );

  protected readonly formulario = this.fb.nonNullable.group({
    nombre: ['', [Validators.required, Validators.maxLength(120)]],
    municipio: ['', [Validators.required, Validators.maxLength(120)]],
    departamento: ['', [Validators.required, Validators.maxLength(120)]],
    pais: ['Guatemala', [Validators.required, Validators.maxLength(80)]],
    latitud: [0, [Validators.required, Validators.min(-90), Validators.max(90)]],
    longitud: [0, [Validators.required, Validators.min(-180), Validators.max(180)]],
    poblacion: [0, [Validators.min(0)]],
    descripcion: ['', Validators.maxLength(500)],
    activa: [true],
  });

  async ngOnInit(): Promise<void> {
    this.todas.set(await firstValueFrom(this.api.listarComunidades()));
    await this.cargar();
  }

  protected async cargar(): Promise<void> {
    this.cargando.set(true);

    try {
      const estado = this.estadoFiltro();
      this.comunidades.set(
        await firstValueFrom(
          this.api.listarComunidades({
            busqueda: this.busqueda().trim() || undefined,
            activa: estado === '' ? undefined : estado === 'true',
            municipio: this.municipioFiltro() || undefined,
            departamento: this.departamentoFiltro() || undefined,
          }),
        ),
      );
    } finally {
      this.cargando.set(false);
    }
  }

  protected async limpiar(): Promise<void> {
    this.busqueda.set('');
    this.estadoFiltro.set('');
    this.municipioFiltro.set('');
    this.departamentoFiltro.set('');
    await this.cargar();
  }

  // ------------------------------------------------------------------ Formulario

  protected abrirAlta(): void {
    this.editando.set(null);
    this.formulario.reset();
    this.formularioAbierto.set(true);
  }

  protected abrirEdicion(comunidad: Comunidad): void {
    this.editando.set(comunidad);
    this.formulario.setValue({
      nombre: comunidad.nombre,
      municipio: comunidad.municipio,
      departamento: comunidad.departamento,
      pais: comunidad.pais,
      latitud: comunidad.latitud,
      longitud: comunidad.longitud,
      poblacion: comunidad.poblacion,
      descripcion: comunidad.descripcion ?? '',
      activa: comunidad.activa,
    });
    this.formularioAbierto.set(true);
  }

  protected cerrar(): void {
    this.formularioAbierto.set(false);
    this.editando.set(null);
  }

  protected async guardar(): Promise<void> {
    if (this.formulario.invalid || this.guardando()) {
      this.formulario.markAllAsTouched();
      return;
    }

    this.guardando.set(true);
    const datos = this.formulario.getRawValue();
    const cuerpo = { ...datos, descripcion: datos.descripcion.trim() || null };

    try {
      const enEdicion = this.editando();

      if (enEdicion) {
        await firstValueFrom(this.api.actualizarComunidad(enEdicion.id, cuerpo));
        this.notificaciones.informar('Comunidad actualizada', `${datos.nombre} quedó guardada.`);
      } else {
        await firstValueFrom(this.api.crearComunidad(cuerpo));
        this.notificaciones.informar('Comunidad creada', `${datos.nombre} ya puede recibir sensores.`);
      }

      await this.refrescarTodo();
      this.cerrar();
    } finally {
      this.guardando.set(false);
    }
  }

  protected async alternarEstado(comunidad: Comunidad): Promise<void> {
    if (comunidad.activa) {
      const confirmado = confirm(
        `Desactivar ${comunidad.nombre}.\n\n` +
          'Sus sensores dejarán de generar lecturas y sus alertas abiertas se cerrarán. ' +
          'El historial se conserva. ¿Desea continuar?',
      );
      if (!confirmado) return;
    }

    await firstValueFrom(this.api.cambiarEstadoComunidad(comunidad.id, !comunidad.activa));
    await this.refrescarTodo();
  }

  /** El selector de comunidad del tablero también debe enterarse de los cambios. */
  private async refrescarTodo(): Promise<void> {
    this.todas.set(await firstValueFrom(this.api.listarComunidades()));
    await this.cargar();
    await this.estado.cargarComunidades();
  }

  private unicos(valores: string[]): string[] {
    return [...new Set(valores.filter((v) => v))].sort((a, b) => a.localeCompare(b));
  }
}

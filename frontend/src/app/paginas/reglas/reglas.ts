import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import {
  FENOMENOS,
  FENOMENOS_PROTOCOLO,
  NIVELES,
  NivelAlerta,
  ORDEN_TIPOS_SENSOR,
  ReglaAlerta,
  SENSORES,
  TipoFenomeno,
  TipoSensor,
} from '../../core/modelos/modelos';
import { Api } from '../../core/servicios/api';
import { Autenticacion } from '../../core/servicios/autenticacion';
import { Notificaciones } from '../../core/servicios/notificaciones';

/**
 * Reglas de alerta configurables. Cada regla compara la lectura de los sensores de un tipo
 * con un rango; si la lectura cae dentro, se levanta una alerta del nivel y fenómeno
 * indicados. Conviven con las reglas integradas del motor, que combinan varias magnitudes.
 */
@Component({
  selector: 'app-reglas',
  imports: [ReactiveFormsModule, FormsModule, DatePipe],
  changeDetection: ChangeDetectionStrategy.Eager,
  templateUrl: './reglas.html',
})
export class Reglas implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(Api);
  private readonly notificaciones = inject(Notificaciones);

  protected readonly auth = inject(Autenticacion);

  protected readonly reglas = signal<ReglaAlerta[]>([]);
  protected readonly cargando = signal(false);
  protected readonly guardando = signal(false);
  protected readonly formularioAbierto = signal(false);
  protected readonly editando = signal<ReglaAlerta | null>(null);

  // --- Filtros ---
  protected readonly busqueda = signal('');
  protected readonly tipoFiltro = signal<TipoSensor | ''>('');
  protected readonly fenomenoFiltro = signal<TipoFenomeno | ''>('');
  protected readonly nivelFiltro = signal<NivelAlerta | ''>('');
  protected readonly estadoFiltro = signal<'' | 'true' | 'false'>('');

  protected readonly tipos = ORDEN_TIPOS_SENSOR.map((t) => ({ valor: t, ...SENSORES[t] }));
  protected readonly fenomenos = FENOMENOS_PROTOCOLO.map((f) => ({ valor: f, ...FENOMENOS[f] }));
  protected readonly niveles = (Object.keys(NIVELES) as NivelAlerta[])
    .filter((n) => n !== 'Verde')
    .map((n) => ({ valor: n, ...NIVELES[n] }));

  protected readonly sensores = SENSORES;
  protected readonly fenomenoMeta = FENOMENOS;
  protected readonly nivelMeta = NIVELES;

  protected readonly formulario = this.fb.group({
    nombre: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(120)]),
    tipoSensor: this.fb.nonNullable.control<TipoSensor>('NivelRio', Validators.required),
    valorMinimo: this.fb.control<number | null>(null),
    valorMaximo: this.fb.control<number | null>(null),
    nivel: this.fb.nonNullable.control<NivelAlerta>('Amarillo', Validators.required),
    fenomeno: this.fb.nonNullable.control<TipoFenomeno>('Inundacion', Validators.required),
    mensaje: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(500)]),
    activa: this.fb.nonNullable.control(true),
  });

  async ngOnInit(): Promise<void> {
    await this.cargar();
  }

  protected async cargar(): Promise<void> {
    this.cargando.set(true);

    try {
      const estado = this.estadoFiltro();
      this.reglas.set(
        await firstValueFrom(
          this.api.reglas({
            busqueda: this.busqueda().trim() || undefined,
            tipoSensor: this.tipoFiltro() || undefined,
            fenomeno: this.fenomenoFiltro() || undefined,
            nivel: this.nivelFiltro() || undefined,
            activa: estado === '' ? undefined : estado === 'true',
          }),
        ),
      );
    } finally {
      this.cargando.set(false);
    }
  }

  protected async limpiar(): Promise<void> {
    this.busqueda.set('');
    this.tipoFiltro.set('');
    this.fenomenoFiltro.set('');
    this.nivelFiltro.set('');
    this.estadoFiltro.set('');
    await this.cargar();
  }

  /** Rango legible: "≥ 4.5", "≤ 0", "30 – 60". */
  protected rango(regla: ReglaAlerta): string {
    const { valorMinimo: min, valorMaximo: max } = regla;
    if (min !== null && max !== null) return `${min} – ${max}`;
    if (min !== null) return `≥ ${min}`;
    return `≤ ${max}`;
  }

  // ------------------------------------------------------------------ Formulario

  protected abrirAlta(): void {
    this.editando.set(null);
    this.formulario.reset();
    this.formularioAbierto.set(true);
  }

  protected abrirEdicion(regla: ReglaAlerta): void {
    this.editando.set(regla);
    this.formulario.setValue({
      nombre: regla.nombre,
      tipoSensor: regla.tipoSensor,
      valorMinimo: regla.valorMinimo,
      valorMaximo: regla.valorMaximo,
      nivel: regla.nivel,
      fenomeno: regla.fenomeno,
      mensaje: regla.mensaje,
      activa: regla.activa,
    });
    this.formularioAbierto.set(true);
  }

  protected cerrar(): void {
    this.formularioAbierto.set(false);
    this.editando.set(null);
  }

  /** Al menos un extremo del rango es obligatorio, y el mínimo no puede superar al máximo. */
  protected rangoInvalido(): string | null {
    const { valorMinimo: min, valorMaximo: max } = this.formulario.getRawValue();
    if (min === null && max === null) return 'Indique al menos un valor mínimo o máximo.';
    if (min !== null && max !== null && min > max) return 'El mínimo no puede superar al máximo.';
    return null;
  }

  protected async guardar(): Promise<void> {
    if (this.formulario.invalid || this.rangoInvalido() || this.guardando()) {
      this.formulario.markAllAsTouched();
      return;
    }

    this.guardando.set(true);
    const datos = this.formulario.getRawValue();

    try {
      const enEdicion = this.editando();

      if (enEdicion) {
        await firstValueFrom(this.api.actualizarRegla(enEdicion.id, datos));
        this.notificaciones.informar('Regla actualizada', `${datos.nombre} se aplica desde ya.`);
      } else {
        await firstValueFrom(this.api.crearRegla(datos));
        this.notificaciones.informar('Regla creada', `${datos.nombre} se evaluará en cada lectura.`);
      }

      await this.cargar();
      this.cerrar();
    } finally {
      this.guardando.set(false);
    }
  }

  protected async alternarEstado(regla: ReglaAlerta): Promise<void> {
    await firstValueFrom(this.api.cambiarEstadoRegla(regla.id, !regla.activa));
    await this.cargar();
  }

  protected async eliminar(regla: ReglaAlerta): Promise<void> {
    const confirmado = confirm(
      `Eliminar la regla "${regla.nombre}".\n\n` +
        'Las alertas que ya generó conservan su nombre. Si solo quiere dejar de aplicarla, ' +
        'desactívela. ¿Desea eliminarla?',
    );
    if (!confirmado) return;

    await firstValueFrom(this.api.eliminarRegla(regla.id));
    await this.cargar();
  }
}

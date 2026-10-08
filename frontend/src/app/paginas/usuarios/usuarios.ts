import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ROLES, RolUsuario, Usuario } from '../../core/modelos/modelos';
import { Api } from '../../core/servicios/api';
import { Autenticacion } from '../../core/servicios/autenticacion';
import { Notificaciones } from '../../core/servicios/notificaciones';

/**
 * Administración de cuentas. Reservada al rol administrador por la guarda de la ruta y,
 * de forma independiente, por la política del propio controlador de la API: el control de
 * acceso real vive en el servidor, y esconder el menú es solo una comodidad.
 *
 * Las cuentas no se eliminan, se deshabilitan. Borrarlas dejaría huérfanas las alertas
 * reconocidas y los asientos de la bitácora que llevan su nombre.
 */
@Component({
  selector: 'app-usuarios',
  imports: [ReactiveFormsModule, FormsModule, DatePipe],
  templateUrl: './usuarios.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './usuarios.scss',
})
export class Usuarios implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(Api);
  private readonly notificaciones = inject(Notificaciones);

  protected readonly auth = inject(Autenticacion);

  protected readonly usuarios = signal<Usuario[]>([]);
  protected readonly cargando = signal(false);
  protected readonly guardando = signal(false);
  protected readonly formularioAbierto = signal(false);
  protected readonly editando = signal<Usuario | null>(null);

  // --- Filtros ---
  protected readonly busqueda = signal('');
  protected readonly rolFiltro = signal<RolUsuario | ''>('');
  protected readonly estadoFiltro = signal<'' | 'true' | 'false'>('');

  /**
   * Total de administradores activos sin filtros: la protección del último administrador
   * no puede depender de lo que el listado esté mostrando.
   */
  private readonly todos = signal<Usuario[]>([]);

  protected readonly roles = Object.entries(ROLES).map(([valor, meta]) => ({
    valor: valor as RolUsuario,
    ...meta,
  }));

  protected readonly formulario = this.fb.nonNullable.group({
    nombreCompleto: ['', [Validators.required, Validators.maxLength(120)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8)]],
    rol: ['Consulta' as RolUsuario, Validators.required],
  });

  /**
   * Administradores activos que quedarían si se retira el que se está editando. Sirve para
   * avisar en la interfaz antes de que la API rechace la operación.
   */
  protected readonly administradoresActivos = computed(
    () => this.todos().filter((u) => u.rol === 'Administrador' && u.activo).length,
  );

  async ngOnInit(): Promise<void> {
    await this.cargar();
  }

  protected async cargar(): Promise<void> {
    this.cargando.set(true);

    try {
      const estado = this.estadoFiltro();
      const [todos, filtrados] = await Promise.all([
        firstValueFrom(this.api.usuarios()),
        firstValueFrom(
          this.api.usuarios({
            busqueda: this.busqueda().trim() || undefined,
            rol: this.rolFiltro() || undefined,
            activo: estado === '' ? undefined : estado === 'true',
          }),
        ),
      ]);

      this.todos.set(todos);
      this.usuarios.set(filtrados);
    } finally {
      this.cargando.set(false);
    }
  }

  protected async limpiar(): Promise<void> {
    this.busqueda.set('');
    this.rolFiltro.set('');
    this.estadoFiltro.set('');
    await this.cargar();
  }

  protected esUnoMismo(usuario: Usuario): boolean {
    return usuario.id === this.auth.usuario()?.id;
  }

  /** El último administrador activo no puede quedar sin rol ni deshabilitado. */
  protected esUltimoAdministrador(usuario: Usuario): boolean {
    return usuario.rol === 'Administrador' && usuario.activo && this.administradoresActivos() === 1;
  }

  protected descripcionRol(rol: RolUsuario): string {
    return ROLES[rol].descripcion;
  }

  // ------------------------------------------------------------------ Formulario

  protected abrirAlta(): void {
    this.editando.set(null);
    this.formulario.reset({ nombreCompleto: '', email: '', password: '', rol: 'Consulta' });

    this.formulario.controls.email.enable();
    this.formulario.controls.password.enable();
    this.formularioAbierto.set(true);
  }

  protected abrirEdicion(usuario: Usuario): void {
    this.editando.set(usuario);

    this.formulario.setValue({
      nombreCompleto: usuario.nombreCompleto,
      email: usuario.email,
      password: '',
      rol: usuario.rol,
    });

    // El correo identifica la cuenta en la bitácora y es la credencial de acceso: cambiarlo
    // rompería la trazabilidad de lo que esa persona ya hizo.
    this.formulario.controls.email.disable();

    // La contraseña tiene su propia acción; no se edita junto al resto de los datos.
    this.formulario.controls.password.disable();

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

    try {
      const enEdicion = this.editando();

      if (enEdicion) {
        await firstValueFrom(
          this.api.actualizarUsuario(enEdicion.id, {
            nombreCompleto: datos.nombreCompleto,
            rol: datos.rol,
          }),
        );

        this.notificaciones.informar('Cuenta actualizada', `${datos.email} quedó como ${datos.rol}.`);
      } else {
        await firstValueFrom(
          this.api.crearUsuario({
            nombreCompleto: datos.nombreCompleto,
            email: datos.email,
            password: datos.password,
            rol: datos.rol,
          }),
        );

        this.notificaciones.informar('Cuenta creada', `${datos.email} ya puede iniciar sesión.`);
      }

      await this.cargar();
      this.cerrar();
    } finally {
      this.guardando.set(false);
    }
  }

  // ---------------------------------------------------------------- Operaciones

  protected async alternarEstado(usuario: Usuario): Promise<void> {
    if (!usuario.activo) {
      await firstValueFrom(this.api.cambiarEstadoUsuario(usuario.id, true));
      await this.cargar();
      return;
    }

    const confirmado = confirm(
      `Deshabilitar la cuenta de ${usuario.nombreCompleto} (${usuario.email}).\n\n` +
        'No podrá iniciar sesión. Su historial de acciones y las alertas que reconoció se ' +
        'conservan intactos. ¿Desea continuar?',
    );

    if (!confirmado) return;

    await firstValueFrom(this.api.cambiarEstadoUsuario(usuario.id, false));
    await this.cargar();
  }

  protected async restablecerPassword(usuario: Usuario): Promise<void> {
    const nueva = prompt(
      `Contraseña nueva para ${usuario.email} (mínimo 8 caracteres).\n\n` +
        'Comuníquesela por un canal seguro y pídale que la cambie al entrar.',
    );

    if (nueva === null) return;

    if (nueva.length < 8) {
      this.notificaciones.error('La contraseña debe tener al menos 8 caracteres.');
      return;
    }

    await firstValueFrom(this.api.restablecerPassword(usuario.id, nueva));
    this.notificaciones.informar(
      'Contraseña restablecida',
      `${usuario.email} ya puede entrar con la contraseña nueva.`,
    );
  }
}

import { Component, inject, signal, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ProveedorExterno } from '../../core/modelos/modelos';
import { Autenticacion } from '../../core/servicios/autenticacion';
import { Notificaciones } from '../../core/servicios/notificaciones';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule],
  templateUrl: './login.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './login.scss',
})
export class Login implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(Autenticacion);
  private readonly router = inject(Router);
  private readonly ruta = inject(ActivatedRoute);
  private readonly notificaciones = inject(Notificaciones);

  protected readonly enviando = signal(false);
  protected readonly error = signal<string | null>(null);

  /** Google, GitHub u otros: solo aparecen los que el Auth Service tiene configurados. */
  protected readonly proveedores = signal<ProveedorExterno[]>([]);
  protected readonly redirigiendo = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    this.proveedores.set(await this.auth.proveedores());
  }

  protected entrarCon(proveedor: ProveedorExterno): void {
    this.redirigiendo.set(proveedor.id);
    this.auth.iniciarConProveedor(proveedor, this.ruta.snapshot.queryParamMap.get('destino') ?? undefined);
  }

  /** Icono de cada proveedor conocido; el resto usa uno genérico. */
  protected icono(id: string): string {
    return ({ google: 'G', github: '🐙', microsoft: '⊞' } as Record<string, string>)[id] ?? '🔑';
  }

  protected readonly formulario = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(6)]],
  });

  /** Cuentas de demostración. Rellenan el formulario con un clic durante la evaluación. */
  protected readonly cuentasDemo = [
    { etiqueta: 'Administrador', email: 'admin@swmatrc.org', password: 'Admin.2026' },
    { etiqueta: 'Operador', email: 'operador@swmatrc.org', password: 'Operador.2026' },
    { etiqueta: 'Consulta', email: 'consulta@swmatrc.org', password: 'Consulta.2026' },
  ];

  protected usar(cuenta: { email: string; password: string }): void {
    this.formulario.setValue({ email: cuenta.email, password: cuenta.password });
  }

  protected async enviar(): Promise<void> {
    if (this.formulario.invalid || this.enviando()) {
      this.formulario.markAllAsTouched();
      return;
    }

    this.enviando.set(true);
    this.error.set(null);

    const { email, password } = this.formulario.getRawValue();

    try {
      await this.auth.iniciarSesion(email, password);

      // El inicio de sesión es el gesto del usuario que los navegadores exigen antes de
      // permitir reproducir audio: se aprovecha para dejar listo el aviso sonoro.
      this.notificaciones.habilitarAudio();

      const destino = this.ruta.snapshot.queryParamMap.get('destino') ?? '/dashboard';
      await this.router.navigateByUrl(destino);
    } catch (error: unknown) {
      this.error.set(this.mensajeDe(error));
    } finally {
      this.enviando.set(false);
    }
  }

  private mensajeDe(error: unknown): string {
    const respuesta = error as { status?: number; error?: { detail?: string } };

    if (respuesta.status === 0) {
      return 'No hay conexión con el servidor. Verifique que la API esté en marcha.';
    }

    return respuesta.error?.detail ?? 'No fue posible iniciar sesión. Intente de nuevo.';
  }
}

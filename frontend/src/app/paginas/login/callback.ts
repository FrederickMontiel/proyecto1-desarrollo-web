import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Autenticacion } from '../../core/servicios/autenticacion';
import { Notificaciones } from '../../core/servicios/notificaciones';

/** Mensajes para los motivos que el Auth Service manda en `?error=`. */
const MENSAJES: Record<string, string> = {
  'acceso-cancelado': 'Se canceló el inicio de sesión en el proveedor.',
  'solicitud-invalida': 'La solicitud de inicio de sesión no es válida o ya venció. Intente de nuevo.',
  'proveedor-no-disponible': 'El proveedor de identidad no respondió. Intente de nuevo en unos minutos.',
  'proveedor-desconocido': 'El proveedor de identidad no está habilitado.',
  'correo-no-verificado': 'Su cuenta del proveedor no tiene un correo verificado.',
  'no-registrado': 'Su correo no está registrado en el sistema. Solicite acceso al administrador.',
  'cuenta-deshabilitada': 'La cuenta está deshabilitada. Contacte al administrador.',
};

/**
 * Destino de la vuelta desde Google o GitHub. El Auth Service manda aquí un código de un
 * solo uso (`?codigo=`) que se canjea por el JWT, o el motivo del fallo (`?error=`).
 */
@Component({
  selector: 'app-login-callback',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './login.scss',
  template: `
    <div class="acceso acceso--centrado">
      <section class="acceso__formulario">
        <div class="tarjeta formulario">
          @if (error()) {
            <header class="formulario__cabecera">
              <h2>No fue posible iniciar sesión</h2>
            </header>
            <p class="error-general" role="alert">{{ error() }}</p>
            <a class="boton boton--primario" routerLink="/login">Volver al acceso</a>
          } @else {
            <header class="formulario__cabecera">
              <h2>Iniciando sesión…</h2>
              <p class="texto-suave">Validando su identidad con el sistema.</p>
            </header>
          }
        </div>
      </section>
    </div>
  `,
})
export class LoginCallback implements OnInit {
  private readonly auth = inject(Autenticacion);
  private readonly router = inject(Router);
  private readonly ruta = inject(ActivatedRoute);
  private readonly notificaciones = inject(Notificaciones);

  protected readonly error = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    const parametros = this.ruta.snapshot.queryParamMap;
    const motivo = parametros.get('error');
    const codigo = parametros.get('codigo');

    if (motivo || !codigo) {
      this.error.set(MENSAJES[motivo ?? ''] ?? 'No se recibió respuesta del proveedor de identidad.');
      return;
    }

    try {
      const destino = await this.auth.canjearCodigo(codigo);
      this.notificaciones.habilitarAudio();
      // replaceUrl: el código ya se usó; no debe quedar en el historial del navegador.
      await this.router.navigateByUrl(destino, { replaceUrl: true });
    } catch (e: unknown) {
      const respuesta = e as { error?: { detail?: string } };
      this.error.set(respuesta.error?.detail ?? 'No fue posible completar el inicio de sesión.');
    }
  }
}

import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RolUsuario, Sesion, Usuario } from '../modelos/modelos';

const CLAVE_SESION = 'swmatrc.sesion';

/**
 * Estado de sesión de la aplicación. Conserva el token en `localStorage` para que una
 * recarga de página no expulse al usuario, y descarta por sí sola las sesiones vencidas
 * al arrancar en lugar de esperar al primer 401.
 */
@Injectable({ providedIn: 'root' })
export class Autenticacion {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly sesion = signal<Sesion | null>(this.recuperarSesion());

  readonly usuario = computed<Usuario | null>(() => this.sesion()?.usuario ?? null);
  readonly autenticado = computed(() => this.sesion() !== null);
  readonly rol = computed<RolUsuario | null>(() => this.sesion()?.usuario.rol ?? null);

  /** Puede operar sobre sensores y reconocer alertas. */
  readonly puedeOperar = computed(() => {
    const rol = this.rol();
    return rol === 'Operador' || rol === 'Administrador';
  });

  /** Puede administrar la red, los usuarios y reiniciar el sistema. */
  readonly puedeAdministrar = computed(() => this.rol() === 'Administrador');

  get token(): string | null {
    return this.sesion()?.token ?? null;
  }

  async iniciarSesion(email: string, password: string): Promise<void> {
    const sesion = await firstValueFrom(
      this.http.post<Sesion>(`${environment.urlApi}/cuenta/login`, { email, password }),
    );

    localStorage.setItem(CLAVE_SESION, JSON.stringify(sesion));
    this.sesion.set(sesion);
  }

  cerrarSesion(redirigir = true): void {
    localStorage.removeItem(CLAVE_SESION);
    this.sesion.set(null);

    if (redirigir) {
      void this.router.navigate(['/login']);
    }
  }

  private recuperarSesion(): Sesion | null {
    const guardada = localStorage.getItem(CLAVE_SESION);
    if (!guardada) return null;

    try {
      const sesion = JSON.parse(guardada) as Sesion;

      // Un token caducado no sirve de nada: se descarta aquí para no arrancar la
      // aplicación en un estado aparentemente válido que fallaría en la primera llamada.
      if (new Date(sesion.expira).getTime() <= Date.now()) {
        localStorage.removeItem(CLAVE_SESION);
        return null;
      }

      return sesion;
    } catch {
      localStorage.removeItem(CLAVE_SESION);
      return null;
    }
  }
}

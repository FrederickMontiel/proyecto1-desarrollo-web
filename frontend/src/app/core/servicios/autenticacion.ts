import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ProveedorExterno, RolUsuario, Sesion, Usuario } from '../modelos/modelos';

const CLAVE_SESION = 'swmatrc.sesion';
const CLAVE_DESTINO = 'swmatrc.destino';

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
    return rol === 'OPERATOR' || rol === 'ADMIN';
  });

  /** Puede administrar la red, los usuarios y reiniciar el sistema. */
  readonly puedeAdministrar = computed(() => this.rol() === 'ADMIN');

  /** Permisos efectivos que emitió el Auth Service para la sesión. */
  readonly permisos = computed<string[]>(() => this.sesion()?.usuario.permisos ?? []);

  /** Genera reportes PDF (Operador y Administrador). */
  readonly puedeGenerarReportes = computed(() => this.tiene('reportes:generar'));

  /** RF-REP-10: la descarga de reportes es exclusiva de los administradores. */
  readonly puedeDescargarReportes = computed(() => this.tiene('reportes:descargar'));

  tiene(permiso: string): boolean {
    return this.permisos().includes(permiso);
  }

  get token(): string | null {
    return this.sesion()?.token ?? null;
  }

  async iniciarSesion(email: string, password: string): Promise<void> {
    const sesion = await firstValueFrom(
      this.http.post<Sesion>(`${environment.urlApi}/auth/login`, { email, password }),
    );
    this.guardar(sesion);
  }

  /** Proveedores externos habilitados (Google, GitHub…). Si la API no responde, ninguno. */
  async proveedores(): Promise<ProveedorExterno[]> {
    try {
      return await firstValueFrom(this.http.get<ProveedorExterno[]>(`${environment.urlApi}/auth/providers`));
    } catch {
      return [];
    }
  }

  /**
   * Inicia OAuth 2.0 / OpenID Connect: el navegador sale de la aplicación hacia el Auth
   * Service, que lo redirige al proveedor. Vuelve a /login/callback con un código.
   */
  iniciarConProveedor(proveedor: ProveedorExterno, destino?: string): void {
    if (destino) sessionStorage.setItem(CLAVE_DESTINO, destino);
    window.location.assign(proveedor.url);
  }

  /** Canjea el código de un solo uso del callback por el JWT del sistema. */
  async canjearCodigo(codigo: string): Promise<string> {
    const sesion = await firstValueFrom(
      this.http.post<Sesion>(`${environment.urlApi}/auth/exchange`, { codigo }),
    );
    this.guardar(sesion);

    const destino = sessionStorage.getItem(CLAVE_DESTINO) ?? '/dashboard';
    sessionStorage.removeItem(CLAVE_DESTINO);
    // Solo rutas internas: un destino manipulado no puede sacar al usuario del sitio.
    return destino.startsWith('/') && !destino.startsWith('//') ? destino : '/dashboard';
  }

  private guardar(sesion: Sesion): void {
    localStorage.setItem(CLAVE_SESION, JSON.stringify(sesion));
    this.sesion.set(sesion);
  }

  /**
   * Cierre de sesión iniciado por el usuario: se avisa a la API para que quede en la
   * bitácora y luego se descarta el token. Si la llamada falla —red caída, token ya
   * vencido—, la sesión local se cierra igualmente.
   */
  async salir(): Promise<void> {
    if (this.token) {
      try {
        await firstValueFrom(this.http.post<void>(`${environment.urlApi}/auth/logout`, null));
      } catch {
        // El cierre local no depende de que el servidor responda.
      }
    }

    this.cerrarSesion();
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

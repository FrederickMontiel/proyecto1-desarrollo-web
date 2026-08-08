import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { Autenticacion } from '../servicios/autenticacion';
import { Notificaciones } from '../servicios/notificaciones';

/** Adjunta el token de sesión a toda llamada dirigida a la API. */
export const interceptorToken: HttpInterceptorFn = (peticion, siguiente) => {
  const token = inject(Autenticacion).token;

  if (!token || !peticion.url.startsWith('/api')) {
    return siguiente(peticion);
  }

  return siguiente(
    peticion.clone({ setHeaders: { Authorization: `Bearer ${token}` } }),
  );
};

/**
 * Traduce los errores HTTP a avisos legibles. La API responde `ProblemDetails`, así que
 * el mensaje útil viene en `detail`; el texto genérico solo aparece cuando no hay ninguno.
 */
export const interceptorErrores: HttpInterceptorFn = (peticion, siguiente) => {
  const auth = inject(Autenticacion);
  const notificaciones = inject(Notificaciones);

  return siguiente(peticion).pipe(
    catchError((error: HttpErrorResponse) => {
      // El login gestiona su propio error dentro del formulario.
      const esLogin = peticion.url.includes('/cuenta/login');

      if (error.status === 401 && !esLogin) {
        // Token vencido o revocado: se cierra la sesión en lugar de dejar la aplicación
        // en un estado en el que ninguna petición funciona.
        auth.cerrarSesion();
        notificaciones.error('La sesión expiró. Vuelva a iniciar sesión.');
        return throwError(() => error);
      }

      if (!esLogin) {
        notificaciones.error(mensajeDe(error));
      }

      return throwError(() => error);
    }),
  );
};

function mensajeDe(error: HttpErrorResponse): string {
  if (error.status === 0) {
    return 'Sin conexión con el servidor. Verifique que la API esté disponible.';
  }

  const detalle = error.error?.detail ?? error.error?.title;
  if (typeof detalle === 'string' && detalle.length > 0) {
    return detalle;
  }

  return `Error ${error.status} al procesar la solicitud.`;
}

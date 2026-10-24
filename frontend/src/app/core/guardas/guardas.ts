import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Autenticacion } from '../servicios/autenticacion';

/** Exige sesión iniciada y recuerda el destino para volver a él tras el login. */
export const guardaAutenticado: CanActivateFn = (_ruta, estado) => {
  const auth = inject(Autenticacion);
  const router = inject(Router);

  if (auth.autenticado()) return true;

  return router.createUrlTree(['/login'], { queryParams: { destino: estado.url } });
};

/** Reserva una ruta al rol administrador. */
export const guardaAdministrador: CanActivateFn = () => {
  const auth = inject(Autenticacion);
  const router = inject(Router);

  return auth.puedeAdministrar() ? true : router.createUrlTree(['/dashboard']);
};

/** Reserva una ruta a quien tenga el permiso indicado (lo emite el Auth Service en el token). */
export function guardaPermiso(permiso: string): CanActivateFn {
  return () => {
    const auth = inject(Autenticacion);
    const router = inject(Router);

    return auth.tiene(permiso) ? true : router.createUrlTree(['/dashboard']);
  };
}

/** Evita que una sesión activa vuelva a la pantalla de acceso. */
export const guardaAnonimo: CanActivateFn = () => {
  const auth = inject(Autenticacion);
  const router = inject(Router);

  return auth.autenticado() ? router.createUrlTree(['/dashboard']) : true;
};

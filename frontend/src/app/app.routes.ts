import { Routes } from '@angular/router';
import { guardaAdministrador, guardaAnonimo, guardaAutenticado } from './core/guardas/guardas';

/**
 * Todas las vistas se cargan de forma diferida: el paquete inicial se limita a la
 * pantalla de acceso, que es lo único que un usuario sin sesión necesita descargar.
 */
export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guardaAnonimo],
    title: 'Acceso — SWMATRC',
    loadComponent: () => import('./paginas/login/login').then((m) => m.Login),
  },
  {
    path: '',
    canActivate: [guardaAutenticado],
    loadComponent: () => import('./disposicion/shell/shell').then((m) => m.Shell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        title: 'Monitoreo — SWMATRC',
        loadComponent: () => import('./paginas/dashboard/dashboard').then((m) => m.Dashboard),
      },
      {
        path: 'alertas',
        title: 'Alertas — SWMATRC',
        loadComponent: () => import('./paginas/alertas/alertas').then((m) => m.Alertas),
      },
      {
        path: 'historial',
        title: 'Historial de eventos — SWMATRC',
        loadComponent: () => import('./paginas/historial/historial').then((m) => m.Historial),
      },
      {
        path: 'comunidades',
        title: 'Comunidades — SWMATRC',
        loadComponent: () => import('./paginas/comunidades/comunidades').then((m) => m.Comunidades),
      },
      {
        path: 'sensores',
        title: 'Sensores — SWMATRC',
        loadComponent: () => import('./paginas/sensores/sensores').then((m) => m.Sensores),
      },
      {
        path: 'lecturas',
        title: 'Lecturas — SWMATRC',
        loadComponent: () => import('./paginas/lecturas/lecturas').then((m) => m.Lecturas),
      },
      {
        path: 'reglas',
        title: 'Reglas de alerta — SWMATRC',
        loadComponent: () => import('./paginas/reglas/reglas').then((m) => m.Reglas),
      },
      {
        path: 'usuarios',
        canActivate: [guardaAdministrador],
        title: 'Usuarios — SWMATRC',
        loadComponent: () => import('./paginas/usuarios/usuarios').then((m) => m.Usuarios),
      },
      {
        path: 'bitacora',
        canActivate: [guardaAdministrador],
        title: 'Bitácora — SWMATRC',
        loadComponent: () => import('./paginas/bitacora/bitacora').then((m) => m.Bitacora),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];

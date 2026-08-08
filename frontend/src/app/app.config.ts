import { registerLocaleData } from '@angular/common';
import localeEs from '@angular/common/locales/es';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, LOCALE_ID, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { routes } from './app.routes';
import { interceptorErrores, interceptorToken } from './core/interceptores/interceptores';

// Sin esto los pipes formatean en inglés: la coma decimal y el orden de la fecha son
// parte de que el tablero se lea como corresponde al idioma de la aplicación.
registerLocaleData(localeEs);

export const appConfig: ApplicationConfig = {
  providers: [
    { provide: LOCALE_ID, useValue: 'es' },
    provideBrowserGlobalErrorListeners(),
    provideRouter(
      routes,
      // Los parámetros de ruta llegan como @Input a los componentes.
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'top' }),
    ),
    // El orden importa: el token se adjunta antes de que el manejador de errores
    // pueda observar un 401 y cerrar la sesión.
    provideHttpClient(withInterceptors([interceptorToken, interceptorErrores])),
  ],
};

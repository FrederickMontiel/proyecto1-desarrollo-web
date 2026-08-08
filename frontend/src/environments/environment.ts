/**
 * Rutas relativas: en producción nginx sirve la SPA y hace de proxy hacia la API en el
 * mismo origen, así que no hay host que configurar ni CORS que resolver. En desarrollo
 * el proxy de `ng serve` (proxy.conf.json) reproduce ese mismo esquema.
 */
export const environment = {
  produccion: true,
  urlApi: '/api',
  urlHub: '/hubs/monitoreo',
};

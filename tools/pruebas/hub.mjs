// Se conecta al hub de monitoreo a través del gateway, igual que el frontend, y escribe en
// stdout un JSON con lo recibido. Lo invoca e2e.py.
//
//   node hub.mjs <url-gateway> <token> <comunidadId> <segundos>
import * as signalR from '@microsoft/signalr';

const [, , gateway, token, comunidadId, segundos = '8'] = process.argv;

const conexion = new signalR.HubConnectionBuilder()
  .withUrl(`${gateway}/hubs/monitoring`, {
    accessTokenFactory: () => token,
    transport: signalR.HttpTransportType.WebSockets,
    skipNegotiation: true,
  })
  .configureLogging(signalR.LogLevel.None)
  .build();

const recibidos = {};
let estado = null;
for (const evento of ['EstadoComunidad', 'LecturaRecibida', 'EstadoSensorCambiado', 'AlertaGenerada', 'AlertaCerrada', 'AlertaAtendida']) {
  conexion.on(evento, (datos) => {
    recibidos[evento] = (recibidos[evento] ?? 0) + 1;
    if (evento === 'EstadoComunidad') estado = { comunidadNombre: datos.comunidadNombre, sensores: datos.sensores.length };
  });
}

const salida = { conectado: false, ping: null, estado, recibidos, error: null };
try {
  await conexion.start();
  salida.conectado = true;
  salida.ping = await conexion.invoke('Ping');
  await conexion.invoke('SuscribirComunidad', Number(comunidadId));
  await conexion.invoke('SuscribirAlertasGlobales');
  await new Promise((r) => setTimeout(r, Number(segundos) * 1000));
} catch (error) {
  salida.error = error.message;
} finally {
  salida.estado = estado;
  await conexion.stop().catch(() => {});
  console.log(JSON.stringify(salida));
}

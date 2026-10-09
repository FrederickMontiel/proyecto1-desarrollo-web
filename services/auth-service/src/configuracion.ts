import { readFileSync } from 'node:fs';

/**
 * Lee un valor sensible. Si existe la variable `<NOMBRE>_FILE`, el valor sale de ese
 * archivo: es como Docker y Kubernetes montan los Secrets. Si no, se usa la variable
 * normal. Así el mismo contenedor sirve en Compose (variables) y en K8s (archivos).
 */
export function leerSecreto(nombre: string): string | undefined {
  const archivo = process.env[`${nombre}_FILE`];
  if (archivo) return readFileSync(archivo, 'utf8').trim();
  return process.env[nombre];
}

function requerido(nombre: string, valor: string | undefined): string {
  if (!valor) throw new Error(`Falta la variable de entorno ${nombre}.`);
  return valor;
}

/**
 * Credenciales de los servicios internos que pueden pedir un token de servicio.
 * Formato: `id:secreto,id:secreto`.
 */
function leerClientesServicio(): Map<string, string> {
  const clientes = new Map<string, string>();
  const crudo = leerSecreto('CLIENTES_SERVICIO') ?? '';

  for (const par of crudo.split(',').map((p) => p.trim()).filter(Boolean)) {
    const separador = par.indexOf(':');
    if (separador <= 0) throw new Error(`Entrada inválida en CLIENTES_SERVICIO: "${par}".`);
    clientes.set(par.slice(0, separador), par.slice(separador + 1));
  }

  return clientes;
}

export const configuracion = () => ({
  puerto: Number(process.env.PORT ?? 3000),

  baseDatos: {
    host: process.env.DB_HOST ?? 'localhost',
    puerto: Number(process.env.DB_PORT ?? 5432),
    nombre: process.env.DB_NOMBRE ?? 'auth_db',
    usuario: process.env.DB_USUARIO ?? 'auth_svc',
    password: requerido('DB_PASSWORD', leerSecreto('DB_PASSWORD')),
  },

  jwt: {
    emisor: process.env.JWT_EMISOR ?? 'swmatrc-auth',
    audiencia: process.env.JWT_AUDIENCIA ?? 'swmatrc-web',
    // Los tokens de servicio llevan otra audiencia: el gateway solo acepta la del frontend,
    // así que un token interno no sirve para entrar desde Internet.
    audienciaInterna: process.env.JWT_AUDIENCIA_INTERNA ?? 'swmatrc-internal',
    minutosVigencia: Number(process.env.JWT_MINUTOS_VIGENCIA ?? 480),
    minutosVigenciaServicio: Number(process.env.JWT_MINUTOS_VIGENCIA_SERVICIO ?? 15),
    archivoLlavePrivada: requerido('JWT_LLAVE_PRIVADA_FILE', process.env.JWT_LLAVE_PRIVADA_FILE),
  },

  // Dirección con la que los demás contenedores llegan a este servicio. Va en el
  // documento de descubrimiento para que sepan dónde está el JWKS.
  urlInterna: process.env.URL_INTERNA ?? 'http://auth-service:3000',

  urlAuditoria: process.env.URL_AUDITORIA ?? 'http://audit-service:8000',

  clientesServicio: leerClientesServicio(),

  administradorInicial: {
    nombre: process.env.ADMIN_NOMBRE ?? 'Administrador del sistema',
    email: process.env.ADMIN_EMAIL,
    password: leerSecreto('ADMIN_PASSWORD'),
  },
});

export type Configuracion = ReturnType<typeof configuracion>;

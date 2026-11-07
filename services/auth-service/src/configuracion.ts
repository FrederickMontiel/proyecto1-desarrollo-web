import { existsSync, readFileSync } from 'node:fs';

/**
 * Lee un valor sensible. Si existe la variable `<NOMBRE>_FILE`, el valor sale de ese
 * archivo: es como Docker y Kubernetes montan los Secrets. Si no, se usa la variable
 * normal. Así el mismo contenedor sirve en Compose (variables) y en K8s (archivos).
 */
export function leerSecreto(nombre: string): string | undefined {
  const archivo = process.env[`${nombre}_FILE`];
  // Un Secret opcional que no existe (p. ej. sin OAuth configurado) equivale a no definirlo.
  if (archivo) return existsSync(archivo) ? readFileSync(archivo, 'utf8').trim() : undefined;
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

/** Cómo se habla con un proveedor de identidad externo. */
export interface ConfigProveedor {
  /** Identificador en las rutas: /api/auth/login/{id}, /api/auth/callback/{id}. */
  id: string;
  /** Nombre que ve el usuario en el botón. */
  nombre: string;
  /** `oidc`: OpenID Connect estándar con descubrimiento. `github`: OAuth 2.0 + API de GitHub. */
  tipo: 'oidc' | 'github';
  clientId: string;
  clientSecret: string;
  /** Emisor OIDC (solo `oidc`): de él se descubren los endpoints y el JWKS. */
  emisor?: string;
  alcances: string;
}

/**
 * Proveedores activos. Cada uno se habilita al definir su Client ID y su Client Secret, que
 * llegan de un Secret de Kubernetes (RNF-OAUTH-01/02). Sin ellos el botón no aparece.
 *
 * Google y GitHub vienen predefinidos. Cualquier otro proveedor OpenID Connect (Microsoft
 * Entra ID, Keycloak, Auth0, GitLab…) se agrega solo con configuración (RF-OAUTH-05):
 *
 *   OAUTH_OIDC_PROVEEDORES=microsoft
 *   OAUTH_MICROSOFT_NOMBRE=Microsoft
 *   OAUTH_MICROSOFT_EMISOR=https://login.microsoftonline.com/<tenant>/v2.0
 *   OAUTH_MICROSOFT_CLIENT_ID=...
 *   OAUTH_MICROSOFT_CLIENT_SECRET=...      (o OAUTH_MICROSOFT_CLIENT_SECRET_FILE)
 */
function leerProveedores(): ConfigProveedor[] {
  const proveedores: ConfigProveedor[] = [];

  const agregar = (id: string, base: Omit<ConfigProveedor, 'id' | 'clientId' | 'clientSecret'>) => {
    const prefijo = `OAUTH_${id.toUpperCase().replace(/[^A-Z0-9]/g, '_')}`;
    const clientId = leerSecreto(`${prefijo}_CLIENT_ID`)?.trim();
    const clientSecret = leerSecreto(`${prefijo}_CLIENT_SECRET`)?.trim();
    if (!clientId || !clientSecret) return;
    proveedores.push({ id, clientId, clientSecret, ...base });
  };

  agregar('google', {
    nombre: 'Google',
    tipo: 'oidc',
    emisor: 'https://accounts.google.com',
    alcances: 'openid email profile',
  });

  agregar('github', { nombre: 'GitHub', tipo: 'github', alcances: 'read:user user:email' });

  const extra = (process.env.OAUTH_OIDC_PROVEEDORES ?? '')
    .split(',')
    .map((p) => p.trim().toLowerCase())
    .filter(Boolean);

  for (const id of extra) {
    if (!/^[a-z0-9-]{2,40}$/.test(id)) throw new Error(`Identificador de proveedor inválido: "${id}".`);
    if (proveedores.some((p) => p.id === id)) continue;
    const prefijo = `OAUTH_${id.toUpperCase().replace(/-/g, '_')}`;
    const emisor = process.env[`${prefijo}_EMISOR`];
    if (!emisor) throw new Error(`Falta ${prefijo}_EMISOR para el proveedor OIDC "${id}".`);
    agregar(id, {
      nombre: process.env[`${prefijo}_NOMBRE`] ?? id,
      tipo: 'oidc',
      emisor,
      alcances: process.env[`${prefijo}_ALCANCES`] ?? 'openid email profile',
    });
  }

  return proveedores;
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

  oauth: {
    /**
     * Dirección pública del sistema (la del Ingress). Con ella se arman las URL de callback
     * que se registran en Google y GitHub: {urlPublica}/api/auth/callback/{proveedor}.
     */
    urlPublica: (process.env.URL_PUBLICA ?? 'http://localhost:8080').replace(/\/+$/, ''),
    /** Quien entra por primera vez con un proveedor externo obtiene una cuenta VIEWER. */
    registroAbierto: (process.env.OAUTH_REGISTRO_ABIERTO ?? 'true').toLowerCase() === 'true',
    proveedores: leerProveedores(),
  },

  administradorInicial: {
    nombre: process.env.ADMIN_NOMBRE ?? 'Administrador del sistema',
    email: process.env.ADMIN_EMAIL,
    password: leerSecreto('ADMIN_PASSWORD'),
  },
});

export type Configuracion = ReturnType<typeof configuracion>;

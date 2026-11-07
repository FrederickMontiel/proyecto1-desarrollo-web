import { createRemoteJWKSet, jwtVerify, type JWTVerifyGetKey } from 'jose';
import type { ConfigProveedor } from '../configuracion.js';

/** Identidad que entrega el proveedor, ya normalizada. */
export interface PerfilExterno {
  /** Identificador estable de la cuenta en el proveedor (`sub` en OIDC, `id` en GitHub). */
  sujeto: string;
  email: string | null;
  /** Solo se vincula o crea una cuenta por correo si el proveedor lo verificó. */
  emailVerificado: boolean;
  nombre: string | null;
  fotoUrl: string | null;
}

/** Datos de la solicitud en curso que el proveedor necesita en cada paso. */
export interface DatosSolicitud {
  estado: string;
  nonce: string;
  /** `code_verifier` de PKCE; al proveedor solo viaja su resumen (`code_challenge`). */
  verificador: string;
  desafio: string;
  urlCallback: string;
}

/**
 * Un proveedor de identidad externo. Agregar uno nuevo (Facebook, por ejemplo, que no es
 * OIDC) es implementar esta interfaz; los proveedores OIDC estándar no requieren código.
 */
export interface ProveedorIdentidad {
  readonly id: string;
  readonly nombre: string;
  /** URL del proveedor a la que se redirige el navegador para iniciar sesión. */
  urlAutorizacion(datos: DatosSolicitud): Promise<string>;
  /** Canjea el `code` del callback y devuelve la identidad verificada. */
  obtenerPerfil(codigo: string, datos: Omit<DatosSolicitud, 'estado' | 'desafio'>): Promise<PerfilExterno>;
}

/** El proveedor respondió algo inesperado o no respondió. */
export class ErrorProveedor extends Error {}

export type Fetch = typeof fetch;

const TIEMPO_ESPERA_MS = 10_000;

async function json<T>(respuesta: Response, contexto: string): Promise<T> {
  if (!respuesta.ok) throw new ErrorProveedor(`${contexto}: HTTP ${respuesta.status}.`);
  return (await respuesta.json()) as T;
}

interface Descubrimiento {
  issuer: string;
  authorization_endpoint: string;
  token_endpoint: string;
  jwks_uri: string;
}

/**
 * Proveedor OpenID Connect genérico (Google, Microsoft, Keycloak…). Todo sale del documento
 * de descubrimiento del emisor: endpoints y llaves públicas. El `id_token` se valida aquí
 * mismo —firma con el JWKS del proveedor, emisor, audiencia = nuestro client_id, vigencia y
 * `nonce`—, así que la identidad no depende de llamadas adicionales.
 */
export class ProveedorOidc implements ProveedorIdentidad {
  readonly id: string;
  readonly nombre: string;
  private descubrimiento?: Promise<Descubrimiento>;
  private jwks?: JWTVerifyGetKey;

  constructor(
    private readonly config: ConfigProveedor,
    private readonly http: Fetch = fetch,
  ) {
    this.id = config.id;
    this.nombre = config.nombre;
  }

  private metadatos(): Promise<Descubrimiento> {
    // Se cachea la promesa; si falla, se descarta para reintentar en el próximo inicio de sesión.
    this.descubrimiento ??= (async () => {
      const url = `${this.config.emisor!.replace(/\/+$/, '')}/.well-known/openid-configuration`;
      return json<Descubrimiento>(
        await this.http(url, { signal: AbortSignal.timeout(TIEMPO_ESPERA_MS) }),
        `Descubrimiento OIDC de ${this.nombre}`,
      );
    })().catch((error: unknown) => {
      this.descubrimiento = undefined;
      throw error instanceof ErrorProveedor ? error : new ErrorProveedor(String(error));
    });
    return this.descubrimiento;
  }

  async urlAutorizacion(datos: DatosSolicitud): Promise<string> {
    const { authorization_endpoint } = await this.metadatos();
    const url = new URL(authorization_endpoint);
    url.search = new URLSearchParams({
      response_type: 'code',
      client_id: this.config.clientId,
      redirect_uri: datos.urlCallback,
      scope: this.config.alcances,
      state: datos.estado,
      nonce: datos.nonce,
      code_challenge: datos.desafio,
      code_challenge_method: 'S256',
      // Deja elegir cuenta aunque el navegador ya tenga una sesión abierta en el proveedor.
      prompt: 'select_account',
    }).toString();
    return url.toString();
  }

  async obtenerPerfil(codigo: string, datos: Omit<DatosSolicitud, 'estado' | 'desafio'>): Promise<PerfilExterno> {
    const meta = await this.metadatos();

    const tokens = await json<{ id_token?: string }>(
      await this.http(meta.token_endpoint, {
        method: 'POST',
        headers: { 'content-type': 'application/x-www-form-urlencoded', accept: 'application/json' },
        body: new URLSearchParams({
          grant_type: 'authorization_code',
          code: codigo,
          redirect_uri: datos.urlCallback,
          client_id: this.config.clientId,
          client_secret: this.config.clientSecret,
          code_verifier: datos.verificador,
        }),
        signal: AbortSignal.timeout(TIEMPO_ESPERA_MS),
      }),
      `Canje del código en ${this.nombre}`,
    );
    if (!tokens.id_token) throw new ErrorProveedor(`${this.nombre} no devolvió un id_token.`);

    this.jwks ??= createRemoteJWKSet(new URL(meta.jwks_uri));
    let claims;
    try {
      ({ payload: claims } = await jwtVerify(tokens.id_token, this.jwks, {
        issuer: meta.issuer,
        audience: this.config.clientId,
        clockTolerance: 60,
      }));
    } catch (error) {
      throw new ErrorProveedor(`id_token de ${this.nombre} inválido: ${(error as Error).message}`);
    }

    if (claims.nonce !== datos.nonce) throw new ErrorProveedor(`El nonce del id_token de ${this.nombre} no coincide.`);
    if (!claims.sub) throw new ErrorProveedor(`El id_token de ${this.nombre} no trae "sub".`);

    const email = typeof claims.email === 'string' ? claims.email.toLowerCase() : null;
    return {
      sujeto: claims.sub,
      email,
      // Google y la mayoría lo informan; si no viene, no se confía en el correo.
      emailVerificado: claims.email_verified === true || claims.email_verified === 'true',
      nombre: typeof claims.name === 'string' ? claims.name : null,
      fotoUrl: typeof claims.picture === 'string' ? claims.picture : null,
    };
  }
}

interface UsuarioGithub {
  id: number;
  login: string;
  name: string | null;
  avatar_url: string | null;
}

interface CorreoGithub {
  email: string;
  primary: boolean;
  verified: boolean;
}

/**
 * GitHub implementa OAuth 2.0 pero no OpenID Connect para las OAuth Apps: no emite
 * `id_token`. La identidad se obtiene con el token de acceso desde su API REST: `/user`
 * (id, nombre, foto) y `/user/emails` (correo principal verificado).
 */
export class ProveedorGithub implements ProveedorIdentidad {
  readonly id: string;
  readonly nombre: string;

  static readonly URL_AUTORIZACION = 'https://github.com/login/oauth/authorize';
  static readonly URL_TOKEN = 'https://github.com/login/oauth/access_token';
  static readonly URL_API = 'https://api.github.com';

  constructor(
    private readonly config: ConfigProveedor,
    private readonly http: Fetch = fetch,
  ) {
    this.id = config.id;
    this.nombre = config.nombre;
  }

  urlAutorizacion(datos: DatosSolicitud): Promise<string> {
    const url = new URL(ProveedorGithub.URL_AUTORIZACION);
    url.search = new URLSearchParams({
      client_id: this.config.clientId,
      redirect_uri: datos.urlCallback,
      scope: this.config.alcances,
      state: datos.estado,
      code_challenge: datos.desafio,
      code_challenge_method: 'S256',
      allow_signup: 'false',
    }).toString();
    return Promise.resolve(url.toString());
  }

  async obtenerPerfil(codigo: string, datos: Omit<DatosSolicitud, 'estado' | 'desafio'>): Promise<PerfilExterno> {
    const tokens = await json<{ access_token?: string; error?: string }>(
      await this.http(ProveedorGithub.URL_TOKEN, {
        method: 'POST',
        headers: { 'content-type': 'application/x-www-form-urlencoded', accept: 'application/json' },
        body: new URLSearchParams({
          client_id: this.config.clientId,
          client_secret: this.config.clientSecret,
          code: codigo,
          redirect_uri: datos.urlCallback,
          code_verifier: datos.verificador,
        }),
        signal: AbortSignal.timeout(TIEMPO_ESPERA_MS),
      }),
      'Canje del código en GitHub',
    );
    // GitHub responde 200 con { error } cuando el código no sirve.
    if (!tokens.access_token) throw new ErrorProveedor(`GitHub rechazó el código: ${tokens.error ?? 'sin detalle'}.`);

    const cabeceras = {
      authorization: `Bearer ${tokens.access_token}`,
      accept: 'application/vnd.github+json',
      'x-github-api-version': '2022-11-28',
      // La API de GitHub exige User-Agent.
      'user-agent': 'swmatrc-auth-service',
    };
    const pedir = async <T>(ruta: string) =>
      json<T>(
        await this.http(`${ProveedorGithub.URL_API}${ruta}`, {
          headers: cabeceras,
          signal: AbortSignal.timeout(TIEMPO_ESPERA_MS),
        }),
        `GitHub ${ruta}`,
      );

    const [usuario, correos] = await Promise.all([pedir<UsuarioGithub>('/user'), pedir<CorreoGithub[]>('/user/emails')]);
    const principal = correos.find((c) => c.primary && c.verified) ?? correos.find((c) => c.verified);

    return {
      sujeto: String(usuario.id),
      email: principal?.email.toLowerCase() ?? null,
      emailVerificado: !!principal,
      nombre: usuario.name ?? usuario.login,
      fotoUrl: usuario.avatar_url,
    };
  }
}

/** Construye los proveedores configurados. */
export function crearProveedores(configs: ConfigProveedor[], http: Fetch = fetch): Map<string, ProveedorIdentidad> {
  return new Map(
    configs.map((c) => [c.id, c.tipo === 'github' ? new ProveedorGithub(c, http) : new ProveedorOidc(c, http)]),
  );
}

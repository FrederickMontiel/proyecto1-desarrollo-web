import { exportJWK, generateKeyPair, SignJWT, type CryptoKey } from 'jose';
import { createServer, type Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import type { ConfigProveedor } from '../configuracion.js';
import { crearProveedores, ErrorProveedor, ProveedorGithub, ProveedorOidc, type Fetch } from './proveedores.js';

const respuestaJson = (cuerpo: unknown, estado = 200) =>
  new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } });

const urlDe = (entrada: string | URL | Request) => (entrada instanceof Request ? entrada.url : entrada.toString());

const DATOS = {
  estado: 'estado-1',
  nonce: 'nonce-1',
  verificador: 'verificador-1',
  desafio: 'desafio-1',
  urlCallback: 'https://sistema-climatico.example/api/auth/callback/google',
};

describe('ProveedorOidc (Google y cualquier OIDC)', () => {
  const EMISOR = 'https://idp.prueba';
  const CONFIG: ConfigProveedor = {
    id: 'google',
    nombre: 'Google',
    tipo: 'oidc',
    clientId: 'cliente-123',
    clientSecret: 'secreto',
    emisor: EMISOR,
    alcances: 'openid email profile',
  };

  let servidorJwks: Server;
  let urlJwks: string;
  let privada: CryptoKey;
  let otraPrivada: CryptoKey;

  beforeAll(async () => {
    const par = await generateKeyPair('RS256');
    privada = par.privateKey;
    otraPrivada = (await generateKeyPair('RS256')).privateKey;
    const jwk = { ...(await exportJWK(par.publicKey)), kid: 'k1', alg: 'RS256', use: 'sig' };

    servidorJwks = createServer((_req, res) => {
      res.setHeader('content-type', 'application/json');
      res.end(JSON.stringify({ keys: [jwk] }));
    });
    await new Promise<void>((listo) => servidorJwks.listen(0, '127.0.0.1', listo));
    urlJwks = `http://127.0.0.1:${(servidorJwks.address() as AddressInfo).port}/jwks`;
  });

  afterAll(() => servidorJwks.close());

  const idToken = (claims: Record<string, unknown>, llave = privada) =>
    new SignJWT({ email: 'Ana@Ejemplo.org', email_verified: true, name: 'Ana', nonce: 'nonce-1', ...claims })
      .setProtectedHeader({ alg: 'RS256', kid: 'k1' })
      .setIssuer(EMISOR)
      .setAudience('cliente-123')
      .setSubject('google-sub-1')
      .setIssuedAt()
      .setExpirationTime('5m')
      .sign(llave);

  /** Simula el descubrimiento y el endpoint de tokens del proveedor. */
  function proveedorFalso(token: () => Promise<string>, peticiones: { url: string; cuerpo?: string }[] = []): Fetch {
    return (async (entrada: string | URL | Request, init?: RequestInit) => {
      const url = urlDe(entrada);
      peticiones.push({ url, cuerpo: (init?.body as URLSearchParams | undefined)?.toString() });
      if (url === `${EMISOR}/.well-known/openid-configuration`)
        return respuestaJson({
          issuer: EMISOR,
          authorization_endpoint: `${EMISOR}/authorize`,
          token_endpoint: `${EMISOR}/token`,
          jwks_uri: urlJwks,
        });
      if (url === `${EMISOR}/token`) return respuestaJson({ access_token: 'x', id_token: await token() });
      return respuestaJson({}, 404);
    }) as Fetch;
  }

  it('arma la URL de autorización con state, nonce y PKCE S256', async () => {
    const proveedor = new ProveedorOidc(CONFIG, proveedorFalso(() => idToken({})));
    const url = new URL(await proveedor.urlAutorizacion(DATOS));

    expect(url.origin + url.pathname).toBe(`${EMISOR}/authorize`);
    expect(Object.fromEntries(url.searchParams)).toMatchObject({
      response_type: 'code',
      client_id: 'cliente-123',
      redirect_uri: DATOS.urlCallback,
      scope: 'openid email profile',
      state: 'estado-1',
      nonce: 'nonce-1',
      code_challenge: 'desafio-1',
      code_challenge_method: 'S256',
    });
    // El secreto nunca viaja al navegador.
    expect(url.toString()).not.toContain('secreto');
  });

  it('canjea el código, valida el id_token y normaliza el perfil', async () => {
    const peticiones: { url: string; cuerpo?: string }[] = [];
    const proveedor = new ProveedorOidc(CONFIG, proveedorFalso(() => idToken({ picture: 'https://foto' }), peticiones));

    const perfil = await proveedor.obtenerPerfil('codigo-1', DATOS);

    expect(perfil).toEqual({
      sujeto: 'google-sub-1',
      email: 'ana@ejemplo.org',
      emailVerificado: true,
      nombre: 'Ana',
      fotoUrl: 'https://foto',
    });
    const canje = new URLSearchParams(peticiones.find((p) => p.url.endsWith('/token'))!.cuerpo);
    expect(canje.get('code')).toBe('codigo-1');
    expect(canje.get('code_verifier')).toBe('verificador-1');
    expect(canje.get('grant_type')).toBe('authorization_code');
  });

  it('rechaza un id_token con otro nonce (reutilizado de otra sesión)', async () => {
    const proveedor = new ProveedorOidc(CONFIG, proveedorFalso(() => idToken({ nonce: 'ajeno' })));
    await expect(proveedor.obtenerPerfil('c', DATOS)).rejects.toBeInstanceOf(ErrorProveedor);
  });

  it('rechaza un id_token firmado con una llave que no es del proveedor', async () => {
    const proveedor = new ProveedorOidc(CONFIG, proveedorFalso(() => idToken({}, otraPrivada)));
    await expect(proveedor.obtenerPerfil('c', DATOS)).rejects.toBeInstanceOf(ErrorProveedor);
  });

  it('rechaza un id_token emitido para otra aplicación (audiencia)', async () => {
    const ajeno = () =>
      new SignJWT({ nonce: 'nonce-1' })
        .setProtectedHeader({ alg: 'RS256', kid: 'k1' })
        .setIssuer(EMISOR)
        .setAudience('otra-app')
        .setSubject('x')
        .setExpirationTime('5m')
        .sign(privada);
    const proveedor = new ProveedorOidc(CONFIG, proveedorFalso(ajeno));
    await expect(proveedor.obtenerPerfil('c', DATOS)).rejects.toBeInstanceOf(ErrorProveedor);
  });

  it('marca el correo como no verificado si el proveedor no lo garantiza', async () => {
    const proveedor = new ProveedorOidc(CONFIG, proveedorFalso(() => idToken({ email_verified: false })));
    expect((await proveedor.obtenerPerfil('c', DATOS)).emailVerificado).toBe(false);
  });

  it('si el descubrimiento falla, lo reintenta en la siguiente solicitud', async () => {
    let intentos = 0;
    const http = (async (entrada: string | URL | Request) => {
      intentos++;
      if (intentos === 1) return respuestaJson({}, 503);
      return proveedorFalso(() => idToken({}))(entrada);
    }) as Fetch;
    const proveedor = new ProveedorOidc(CONFIG, http);

    await expect(proveedor.urlAutorizacion(DATOS)).rejects.toBeInstanceOf(ErrorProveedor);
    await expect(proveedor.urlAutorizacion(DATOS)).resolves.toContain('/authorize');
  });
});

describe('ProveedorGithub (OAuth 2.0 + API)', () => {
  const CONFIG: ConfigProveedor = {
    id: 'github',
    nombre: 'GitHub',
    tipo: 'github',
    clientId: 'gh-cliente',
    clientSecret: 'gh-secreto',
    alcances: 'read:user user:email',
  };

  function githubFalso(opciones: { token?: unknown; correos?: unknown } = {}): Fetch {
    return (async (entrada: string | URL | Request, init?: RequestInit) => {
      const url = urlDe(entrada);
      if (url === ProveedorGithub.URL_TOKEN) return respuestaJson(opciones.token ?? { access_token: 'gho_x' });
      const auth = new Headers(init?.headers).get('authorization');
      if (auth !== 'Bearer gho_x') return respuestaJson({}, 401);
      if (url.endsWith('/user'))
        return respuestaJson({ id: 42, login: 'ana-gh', name: null, avatar_url: 'https://avatar' });
      if (url.endsWith('/user/emails'))
        return respuestaJson(
          opciones.correos ?? [
            { email: 'vieja@ejemplo.org', primary: false, verified: true },
            { email: 'Ana@Ejemplo.org', primary: true, verified: true },
          ],
        );
      return respuestaJson({}, 404);
    }) as Fetch;
  }

  it('arma la URL de autorización de GitHub con PKCE', async () => {
    const url = new URL(await new ProveedorGithub(CONFIG, githubFalso()).urlAutorizacion(DATOS));
    expect(url.origin + url.pathname).toBe(ProveedorGithub.URL_AUTORIZACION);
    expect(url.searchParams.get('scope')).toBe('read:user user:email');
    expect(url.searchParams.get('code_challenge_method')).toBe('S256');
    expect(url.searchParams.get('state')).toBe('estado-1');
  });

  it('obtiene id, nombre (login si no hay nombre) y el correo principal verificado', async () => {
    const perfil = await new ProveedorGithub(CONFIG, githubFalso()).obtenerPerfil('c', DATOS);
    expect(perfil).toEqual({
      sujeto: '42',
      email: 'ana@ejemplo.org',
      emailVerificado: true,
      nombre: 'ana-gh',
      fotoUrl: 'https://avatar',
    });
  });

  it('sin correo verificado no entrega correo', async () => {
    const perfil = await new ProveedorGithub(
      CONFIG,
      githubFalso({ correos: [{ email: 'x@y.z', primary: true, verified: false }] }),
    ).obtenerPerfil('c', DATOS);
    expect(perfil.emailVerificado).toBe(false);
    expect(perfil.email).toBeNull();
  });

  it('GitHub responde 200 con { error } ante un código inválido', async () => {
    const proveedor = new ProveedorGithub(CONFIG, githubFalso({ token: { error: 'bad_verification_code' } }));
    await expect(proveedor.obtenerPerfil('c', DATOS)).rejects.toThrow(/bad_verification_code/);
  });
});

describe('crearProveedores', () => {
  it('usa la clase según el tipo: OIDC genérico o GitHub', () => {
    const proveedores = crearProveedores([
      { id: 'google', nombre: 'Google', tipo: 'oidc', clientId: 'a', clientSecret: 'b', emisor: 'https://x', alcances: '' },
      { id: 'github', nombre: 'GitHub', tipo: 'github', clientId: 'a', clientSecret: 'b', alcances: '' },
      { id: 'microsoft', nombre: 'Microsoft', tipo: 'oidc', clientId: 'a', clientSecret: 'b', emisor: 'https://m', alcances: '' },
    ]);
    expect(proveedores.get('google')).toBeInstanceOf(ProveedorOidc);
    expect(proveedores.get('github')).toBeInstanceOf(ProveedorGithub);
    expect(proveedores.get('microsoft')).toBeInstanceOf(ProveedorOidc);
  });
});

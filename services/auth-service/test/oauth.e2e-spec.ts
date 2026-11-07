/**
 * Flujo OAuth 2.0 / OpenID Connect de punta a punta contra PostgreSQL real:
 *   GET /api/auth/login/{p} → GET /api/auth/callback/{p} → POST /api/auth/exchange
 *
 * El proveedor externo es un doble en proceso (los proveedores reales se prueban en
 * src/oauth/proveedores.spec.ts). Necesita una base VACÍA y desechable:
 *
 *   E2E_DB_HOST=127.0.0.1 E2E_DB_PORT=15432 E2E_DB_NOMBRE=auth_prueba \
 *   E2E_DB_USUARIO=auth_svc E2E_DB_PASSWORD=... npm run test:e2e
 *
 * Sin E2E_DB_HOST la prueba se omite.
 */
import { ValidationPipe, type INestApplication } from '@nestjs/common';
import { Test } from '@nestjs/testing';
import { decodeJwt } from 'jose';
import { generateKeyPairSync } from 'node:crypto';
import { mkdtempSync, writeFileSync } from 'node:fs';
import type { Server } from 'node:http';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import request from 'supertest';
import type { DataSource } from 'typeorm';
import type { PerfilExterno, ProveedorIdentidad } from '../src/oauth/proveedores.js';

const habilitada = !!process.env.E2E_DB_HOST;

/** Perfiles que "devuelve el proveedor" según el código del callback. */
const PERFILES: Record<string, PerfilExterno> = {
  nueva: { sujeto: 'sub-nueva', email: 'Nueva@Ejemplo.org', emailVerificado: true, nombre: 'Persona Nueva', fotoUrl: 'https://foto/nueva' },
  operador: { sujeto: 'sub-operador', email: 'operador.local@ejemplo.org', emailVerificado: true, nombre: 'Op', fotoUrl: null },
  sinVerificar: { sujeto: 'sub-x', email: 'x@ejemplo.org', emailVerificado: false, nombre: 'X', fotoUrl: null },
  deshabilitada: { sujeto: 'sub-off', email: 'off@ejemplo.org', emailVerificado: true, nombre: 'Off', fotoUrl: null },
};

const proveedorFalso: ProveedorIdentidad = {
  id: 'falso',
  nombre: 'Proveedor de prueba',
  urlAutorizacion: (d) => {
    expect(d.desafio).toMatch(/^[\w-]{43}$/); // SHA-256 en base64url
    return Promise.resolve(`https://idp.falso/authorize?state=${d.estado}&nonce=${d.nonce}`);
  },
  obtenerPerfil: (codigo, d) => {
    expect(d.verificador.length).toBeGreaterThanOrEqual(43);
    expect(d.urlCallback).toBe('https://sistema-climatico.example/api/auth/callback/falso');
    const perfil = PERFILES[codigo];
    if (!perfil) return Promise.reject(new Error('código desconocido'));
    return Promise.resolve({ ...perfil, email: perfil.email?.toLowerCase() ?? null });
  },
};

describe.skipIf(!habilitada)('OAuth de punta a punta (Auth Service + PostgreSQL)', () => {
  let app: INestApplication;
  let servidor: Server;
  let db: DataSource;

  beforeAll(async () => {
    const { privateKey } = generateKeyPairSync('rsa', { modulusLength: 2048 });
    const llave = join(mkdtempSync(join(tmpdir(), 'auth-e2e-')), 'privada.pem');
    writeFileSync(llave, privateKey.export({ type: 'pkcs8', format: 'pem' }));

    Object.assign(process.env, {
      DB_HOST: process.env.E2E_DB_HOST,
      DB_PORT: process.env.E2E_DB_PORT ?? '5432',
      DB_NOMBRE: process.env.E2E_DB_NOMBRE,
      DB_USUARIO: process.env.E2E_DB_USUARIO,
      DB_PASSWORD: process.env.E2E_DB_PASSWORD,
      JWT_LLAVE_PRIVADA_FILE: llave,
      URL_PUBLICA: 'https://sistema-climatico.example/',
      // Nadie escucha ahí: la auditoría falla en silencio, como cuando Audit está caído.
      URL_AUDITORIA: 'http://127.0.0.1:9',
      ADMIN_EMAIL: 'admin.e2e@ejemplo.org',
      ADMIN_PASSWORD: 'Admin.E2E.2026',
    });

    const { AppModule } = await import('../src/app.module.js');
    const { OauthService } = await import('../src/oauth/oauth.service.js');
    const { DataSource: ClaseDataSource } = await import('typeorm');

    const modulo = await Test.createTestingModule({ imports: [AppModule] }).compile();
    app = modulo.createNestApplication();
    app.useGlobalPipes(new ValidationPipe({ transform: true, whitelist: true, forbidNonWhitelisted: true }));
    await app.init();
    servidor = app.getHttpServer() as Server;

    app.get(OauthService).usarProveedores([proveedorFalso]);
    db = app.get(ClaseDataSource);
  }, 60_000);

  afterAll(async () => {
    await app?.close();
  });

  /** Recorre login → callback como lo haría el navegador. Devuelve la URL final del frontend. */
  async function viaje(codigo: string, opciones: { cookie?: string; error?: string } = {}) {
    const inicio = await request(servidor).get('/api/auth/login/falso').expect(302);
    const estado = new URL(inicio.headers.location).searchParams.get('state')!;
    const cookie = (inicio.headers['set-cookie'] as unknown as string[])[0];
    expect(cookie).toMatch(/swmatrc_oauth_state=.+; .*HttpOnly/);
    expect(cookie).toContain('Secure');
    expect(cookie).toContain('SameSite=Lax');

    const consulta = opciones.error ? `error=${opciones.error}&state=${estado}` : `code=${codigo}&state=${estado}`;
    const vuelta = await request(servidor)
      .get(`/api/auth/callback/falso?${consulta}`)
      .set('Cookie', opciones.cookie ?? cookie.split(';')[0])
      .expect(302);
    return { destino: new URL(vuelta.headers.location), estado, cookie: cookie.split(';')[0] };
  }

  const canjear = (codigo: string) => request(servidor).post('/api/auth/exchange').send({ codigo });

  it('lista el proveedor configurado', async () => {
    const r = await request(servidor).get('/api/auth/providers').expect(200);
    expect(r.body).toEqual([{ id: 'falso', nombre: 'Proveedor de prueba', url: '/api/auth/login/falso' }]);
  });

  it('primer ingreso: crea la cuenta VIEWER, vincula la identidad y emite el JWT del sistema', async () => {
    const { destino } = await viaje('nueva');
    expect(destino.origin + destino.pathname).toBe('https://sistema-climatico.example/login/callback');
    const codigo = destino.searchParams.get('codigo')!;
    expect(codigo).toBeTruthy();

    const sesion = (await canjear(codigo).expect(200)).body;
    expect(sesion.proveedor).toBe('falso');
    expect(sesion.usuario).toMatchObject({ email: 'nueva@ejemplo.org', rol: 'VIEWER', rolNombre: 'Consulta', fotoUrl: 'https://foto/nueva' });

    const claims = decodeJwt(sesion.token);
    expect(claims).toMatchObject({ email: 'nueva@ejemplo.org', roles: ['VIEWER'], provider: 'falso', iss: 'swmatrc-auth', aud: 'swmatrc-web' });

    // El token sirve en las rutas protegidas, pero VIEWER no administra usuarios.
    await request(servidor).get('/api/auth/me').set('Authorization', `Bearer ${sesion.token}`).expect(200);
    await request(servidor).get('/api/auth/users').set('Authorization', `Bearer ${sesion.token}`).expect(403);

    // El código es de un solo uso.
    await canjear(codigo).expect(401);
  });

  it('segundo ingreso con la misma cuenta externa: mismo usuario, sin duplicar', async () => {
    const { destino } = await viaje('nueva');
    const sesion = (await canjear(destino.searchParams.get('codigo')!).expect(200)).body;
    const [{ total }] = await db.query(`SELECT count(*)::int AS total FROM usuarios WHERE email = 'nueva@ejemplo.org'`);
    const [{ identidades }] = await db.query(`SELECT count(*)::int AS identidades FROM identidades_externas WHERE sujeto = 'sub-nueva'`);
    expect(total).toBe(1);
    expect(identidades).toBe(1);
    expect(sesion.usuario.rol).toBe('VIEWER');
  });

  it('un usuario existente con el mismo correo verificado se vincula y conserva su rol', async () => {
    const [{ id: rolId }] = await db.query(`SELECT id FROM roles WHERE codigo = 'OPERATOR'`);
    await db.query(
      `INSERT INTO usuarios (nombre_completo, email, password_hash, rol_id) VALUES ('Operador Local', 'operador.local@ejemplo.org', 'x', $1)`,
      [rolId],
    );

    const { destino } = await viaje('operador');
    const sesion = (await canjear(destino.searchParams.get('codigo')!).expect(200)).body;
    expect(sesion.usuario).toMatchObject({ email: 'operador.local@ejemplo.org', rol: 'OPERATOR', nombreCompleto: 'Operador Local' });
    expect(decodeJwt(sesion.token).roles).toEqual(['OPERATOR']);
  });

  it('sin correo verificado no se crea ni se vincula ninguna cuenta', async () => {
    const { destino } = await viaje('sinVerificar');
    expect(destino.searchParams.get('error')).toBe('correo-no-verificado');
    const [{ total }] = await db.query(`SELECT count(*)::int AS total FROM usuarios WHERE email = 'x@ejemplo.org'`);
    expect(total).toBe(0);
  });

  it('una cuenta deshabilitada no entra', async () => {
    await viaje('deshabilitada'); // la crea
    await db.query(`UPDATE usuarios SET activo = false WHERE email = 'off@ejemplo.org'`);
    const { destino } = await viaje('deshabilitada');
    expect(destino.searchParams.get('error')).toBe('cuenta-deshabilitada');
  });

  it('CSRF: un callback sin la cookie del navegador que inició el flujo se rechaza', async () => {
    const { destino } = await viaje('nueva', { cookie: 'swmatrc_oauth_state=otro-valor' });
    expect(destino.searchParams.get('error')).toBe('solicitud-invalida');
  });

  it('un state ya usado no sirve otra vez', async () => {
    const { estado, cookie } = await viaje('nueva');
    const r = await request(servidor).get(`/api/auth/callback/falso?code=nueva&state=${estado}`).set('Cookie', cookie).expect(302);
    expect(new URL(r.headers.location).searchParams.get('error')).toBe('solicitud-invalida');
  });

  it('el usuario canceló en el proveedor', async () => {
    const { destino } = await viaje('', { error: 'access_denied' });
    expect(destino.searchParams.get('error')).toBe('acceso-cancelado');
  });

  it('proveedor desconocido', async () => {
    const r = await request(servidor).get('/api/auth/login/inexistente').expect(302);
    expect(new URL(r.headers.location).searchParams.get('error')).toBe('proveedor-desconocido');
  });

  it('una cuenta creada con proveedor externo no tiene contraseña local', async () => {
    await request(servidor).post('/api/auth/login').send({ email: 'nueva@ejemplo.org', password: 'cualquiera' }).expect(401);
  });
});

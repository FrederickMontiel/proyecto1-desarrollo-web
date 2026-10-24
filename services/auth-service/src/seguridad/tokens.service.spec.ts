import type { ConfigService } from '@nestjs/config';
import { createRemoteJWKSet, decodeProtectedHeader, jwtVerify } from 'jose';
import { generateKeyPairSync } from 'node:crypto';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { createServer, type Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import type { Configuracion } from '../configuracion.js';
import { LlavesService } from './llaves.service.js';
import { TokensService } from './tokens.service.js';

function crearConfig(archivoLlave: string): ConfigService<Configuracion, true> {
  const valores: Record<string, unknown> = {
    'jwt.archivoLlavePrivada': archivoLlave,
    jwt: {
      emisor: 'swmatrc-auth',
      audiencia: 'swmatrc-web',
      audienciaInterna: 'swmatrc-internal',
      minutosVigencia: 60,
      minutosVigenciaServicio: 5,
      archivoLlavePrivada: archivoLlave,
    },
  };
  return { get: (clave: string) => valores[clave] } as unknown as ConfigService<Configuracion, true>;
}

function escribirLlave(bits = 2048): string {
  const { privateKey } = generateKeyPairSync('rsa', { modulusLength: bits });
  const archivo = join(mkdtempSync(join(tmpdir(), 'auth-llaves-')), 'privada.pem');
  writeFileSync(archivo, privateKey.export({ type: 'pkcs8', format: 'pem' }));
  return archivo;
}

const SUJETO = { id: 7, email: 'ana@ejemplo.org', nombre: 'Ana', rol: 'Operador', permisos: ['alertas:atender'] };

describe('TokensService', () => {
  const config = crearConfig(escribirLlave());
  const llaves = new LlavesService(config);
  const tokens = new TokensService(config, llaves);

  it('emite un token RS256 con los claims del usuario y lo verifica', async () => {
    const emitido = await tokens.emitirUsuario(SUJETO);

    const claims = await tokens.verificar(emitido.token, 'web');
    expect(claims).toMatchObject({
      sub: '7',
      email: 'ana@ejemplo.org',
      role: 'Operador',
      permissions: ['alertas:atender'],
      iss: 'swmatrc-auth',
      aud: 'swmatrc-web',
      jti: emitido.jti,
    });

    const cabecera = decodeProtectedHeader(emitido.token);
    expect(cabecera.alg).toBe('RS256');
    expect(cabecera.kid).toBe(await llaves.kid());
  });

  it('un token de usuario no sirve como token interno, ni al revés', async () => {
    const deUsuario = await tokens.emitirUsuario(SUJETO);
    const deServicio = await tokens.emitirServicio('report-service');

    await expect(tokens.verificar(deUsuario.token, 'interna')).rejects.toThrow();
    await expect(tokens.verificar(deServicio.token, 'web')).rejects.toThrow();
    await expect(tokens.verificar(deServicio.token, 'interna')).resolves.toMatchObject({
      sub: 'svc:report-service',
      role: 'Servicio',
    });
  });

  it('rechaza un token firmado con otra llave', async () => {
    const otraConfig = crearConfig(escribirLlave());
    const ajeno = await new TokensService(otraConfig, new LlavesService(otraConfig)).emitirUsuario(SUJETO);

    await expect(tokens.verificar(ajeno.token, 'web')).rejects.toThrow();
  });

  it('rechaza un token alterado', async () => {
    const { token } = await tokens.emitirUsuario(SUJETO);
    const [cabecera, carga, firma] = token.split('.');
    const cargaAlterada = Buffer.from(
      JSON.stringify({ ...JSON.parse(Buffer.from(carga, 'base64url').toString()), role: 'Administrador' }),
    ).toString('base64url');

    await expect(tokens.verificar(`${cabecera}.${cargaAlterada}.${firma}`, 'web')).rejects.toThrow();
  });

  it('un tercero valida el token solo con el JWKS publicado', async () => {
    const jwk = await llaves.jwk();
    expect(jwk).not.toHaveProperty('d'); // nunca se publica la parte privada

    const servidor: Server = createServer((_req, res) => {
      res.setHeader('content-type', 'application/json');
      res.end(JSON.stringify({ keys: [jwk] }));
    });
    await new Promise<void>((listo) => servidor.listen(0, '127.0.0.1', listo));

    try {
      const { port } = servidor.address() as AddressInfo;
      const jwks = createRemoteJWKSet(new URL(`http://127.0.0.1:${port}/.well-known/jwks.json`));
      const { token } = await tokens.emitirUsuario(SUJETO);

      const { payload } = await jwtVerify(token, jwks, { issuer: 'swmatrc-auth', audience: 'swmatrc-web' });
      expect(payload.sub).toBe('7');
    } finally {
      servidor.close();
    }
  });

  it('no arranca con una llave RSA de menos de 2048 bits', () => {
    expect(() => new LlavesService(crearConfig(escribirLlave(1024)))).toThrow(/2048/);
  });
});

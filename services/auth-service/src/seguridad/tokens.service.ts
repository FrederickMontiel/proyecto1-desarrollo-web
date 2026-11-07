import { Injectable } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { jwtVerify, SignJWT, type JWTPayload } from 'jose';
import { randomUUID } from 'node:crypto';
import type { Configuracion } from '../configuracion.js';
import { ROL_SERVICIO } from '../usuarios/catalogo.js';
import { LlavesService } from './llaves.service.js';

/** Datos del usuario que viajan dentro del token. */
export interface SujetoToken {
  id: number;
  email: string;
  nombre: string;
  roles: string[];
  permisos: string[];
  /** Con qué se autenticó: `local`, `google`, `github`… */
  proveedor: string;
}

export interface TokenEmitido {
  token: string;
  jti: string;
  emitido: Date;
  expira: Date;
}

/** Claims que este servicio pone en sus tokens, además de los registrados (iss, aud, exp…). */
export interface ClaimsSwmatrc extends JWTPayload {
  email?: string;
  name?: string;
  roles?: string[];
  permissions?: string[];
  provider?: string;
}

export const PERMISO_INTERNO = 'interno';

@Injectable()
export class TokensService {
  private readonly jwt: Configuracion['jwt'];

  constructor(
    config: ConfigService<Configuracion, true>,
    private readonly llaves: LlavesService,
  ) {
    this.jwt = config.get('jwt', { infer: true });
  }

  /**
   * Token de acceso de un usuario. Es el mismo sin importar si entró con contraseña, Google
   * o GitHub: los microservicios solo confían en este emisor, nunca en el token del proveedor.
   * Claims como en la sección 6 del documento: `sub`, `email`, `name`, `roles`, `provider`,
   * `iss`, `aud`, `exp`, más `permissions`, `iat` y `jti`.
   */
  emitirUsuario(sujeto: SujetoToken): Promise<TokenEmitido> {
    return this.firmar(
      String(sujeto.id),
      this.jwt.audiencia,
      this.jwt.minutosVigencia,
      {
        email: sujeto.email,
        name: sujeto.nombre,
        roles: sujeto.roles,
        permissions: sujeto.permisos,
        provider: sujeto.proveedor,
      },
    );
  }

  /** Token de corta duración para llamadas entre microservicios. */
  emitirServicio(clienteId: string): Promise<TokenEmitido> {
    return this.firmar(
      `svc:${clienteId}`,
      this.jwt.audienciaInterna,
      this.jwt.minutosVigenciaServicio,
      { name: clienteId, roles: [ROL_SERVICIO], permissions: [PERMISO_INTERNO], provider: 'internal' },
    );
  }

  /** Verifica firma, emisor, audiencia y vigencia. Lanza si algo no cuadra. */
  async verificar(token: string, audiencia: 'web' | 'interna'): Promise<ClaimsSwmatrc> {
    const { payload } = await jwtVerify<ClaimsSwmatrc>(token, this.llaves.publica, {
      issuer: this.jwt.emisor,
      audience: audiencia === 'web' ? this.jwt.audiencia : this.jwt.audienciaInterna,
      algorithms: ['RS256'],
      clockTolerance: 60,
    });
    return payload;
  }

  private async firmar(
    sub: string,
    audiencia: string,
    minutos: number,
    claims: ClaimsSwmatrc,
  ): Promise<TokenEmitido> {
    const jti = randomUUID();
    const emitido = new Date();
    const expira = new Date(emitido.getTime() + minutos * 60_000);

    const token = await new SignJWT(claims)
      .setProtectedHeader({ alg: 'RS256', typ: 'JWT', kid: await this.llaves.kid() })
      .setSubject(sub)
      .setIssuer(this.jwt.emisor)
      .setAudience(audiencia)
      .setJti(jti)
      .setIssuedAt(Math.floor(emitido.getTime() / 1000))
      .setExpirationTime(Math.floor(expira.getTime() / 1000))
      .sign(this.llaves.privada);

    return { token, jti, emitido, expira };
  }
}

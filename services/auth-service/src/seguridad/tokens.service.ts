import { Injectable } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { jwtVerify, SignJWT, type JWTPayload } from 'jose';
import { randomUUID } from 'node:crypto';
import type { Configuracion } from '../configuracion.js';
import { LlavesService } from './llaves.service.js';

/** Datos del usuario que viajan dentro del token. */
export interface SujetoToken {
  id: number;
  email: string;
  nombre: string;
  rol: string;
  permisos: string[];
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
  role?: string;
  permissions?: string[];
}

/** Rol con el que se identifican los tokens de servicio a servicio. */
export const ROL_SERVICIO = 'Servicio';
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
   * Token de acceso de un usuario. Los nombres de los claims son los estándar de OIDC
   * (`sub`, `email`, `name`) para que en la Fase 4 un proveedor externo encaje sin cambios
   * en los validadores.
   */
  emitirUsuario(sujeto: SujetoToken): Promise<TokenEmitido> {
    return this.firmar(
      String(sujeto.id),
      this.jwt.audiencia,
      this.jwt.minutosVigencia,
      { email: sujeto.email, name: sujeto.nombre, role: sujeto.rol, permissions: sujeto.permisos },
    );
  }

  /** Token de corta duración para llamadas entre microservicios. */
  emitirServicio(clienteId: string): Promise<TokenEmitido> {
    return this.firmar(
      `svc:${clienteId}`,
      this.jwt.audienciaInterna,
      this.jwt.minutosVigenciaServicio,
      { name: clienteId, role: ROL_SERVICIO, permissions: [PERMISO_INTERNO] },
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

import { Injectable, Logger } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { calculateJwkThumbprint, type JWK } from 'jose';
import { createPrivateKey, createPublicKey, type KeyObject } from 'node:crypto';
import { readFileSync } from 'node:fs';
import type { Configuracion } from '../configuracion.js';

/**
 * Par de llaves RS256 con el que se firman los tokens. La privada se monta como secreto
 * (archivo) y nunca sale de este servicio; la pública se publica en el JWKS para que el
 * gateway y los demás servicios validen sin compartir ningún secreto.
 *
 * El `kid` es la huella (RFC 7638) de la llave pública: si se rota la llave, cambia solo y
 * los validadores saben que deben volver a descargar el JWKS.
 */
@Injectable()
export class LlavesService {
  private readonly logger = new Logger(LlavesService.name);

  readonly privada: KeyObject;
  readonly publica: KeyObject;
  private jwkPublica?: JWK;

  constructor(config: ConfigService<Configuracion, true>) {
    const archivo = config.get('jwt.archivoLlavePrivada', { infer: true });

    this.privada = createPrivateKey(readFileSync(archivo, 'utf8'));
    if (this.privada.asymmetricKeyType !== 'rsa')
      throw new Error('La llave de firma debe ser RSA (RS256).');
    if ((this.privada.asymmetricKeyDetails?.modulusLength ?? 0) < 2048)
      throw new Error('La llave RSA debe tener al menos 2048 bits.');

    this.publica = createPublicKey(this.privada);
    this.logger.log(`Llave de firma cargada desde ${archivo}.`);
  }

  /** JWK pública con su `kid`, lista para el documento JWKS. */
  async jwk(): Promise<JWK> {
    if (!this.jwkPublica) {
      const jwk = this.publica.export({ format: 'jwk' }) as JWK;
      this.jwkPublica = { ...jwk, kid: await calculateJwkThumbprint(jwk), alg: 'RS256', use: 'sig' };
    }
    return this.jwkPublica;
  }

  async kid(): Promise<string> {
    return (await this.jwk()).kid!;
  }
}

import { Controller, Get, Header } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { Publico } from '../comun/decoradores.js';
import type { Configuracion } from '../configuracion.js';
import { LlavesService } from './llaves.service.js';

/**
 * Documentos de descubrimiento con el formato de OpenID Connect. Los consume el gateway
 * (y en adelante cualquier servicio) para obtener la llave pública. Cuando en la Fase 4
 * se use un proveedor externo, los validadores solo cambian la URL de estos documentos.
 *
 * No se exponen por el gateway: los servicios los leen dentro de la red interna.
 */
@Controller('.well-known')
@Publico()
export class DescubrimientoController {
  constructor(
    private readonly llaves: LlavesService,
    private readonly config: ConfigService<Configuracion, true>,
  ) {}

  @Get('jwks.json')
  @Header('Cache-Control', 'public, max-age=300')
  async jwks() {
    return { keys: [await this.llaves.jwk()] };
  }

  @Get('openid-configuration')
  @Header('Cache-Control', 'public, max-age=300')
  configuracion() {
    const base = this.config.get('urlInterna', { infer: true });
    return {
      issuer: this.config.get('jwt.emisor', { infer: true }),
      jwks_uri: `${base}/.well-known/jwks.json`,
      token_endpoint: `${base}/api/auth/login`,
      response_types_supported: ['token'],
      subject_types_supported: ['public'],
      id_token_signing_alg_values_supported: ['RS256'],
      claims_supported: ['sub', 'email', 'name', 'role', 'permissions'],
    };
  }
}

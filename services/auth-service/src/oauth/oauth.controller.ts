import { Body, Controller, Get, HttpCode, HttpStatus, Logger, Param, Post, Query, Req, Res } from '@nestjs/common';
import type { Request, Response } from 'express';
import { Contexto, Publico, type ContextoPeticion } from '../comun/decoradores.js';
import { CanjearCodigoDto } from '../usuarios/dto.js';
import { FalloOauth, OauthService } from './oauth.service.js';

/** Cookie que liga el callback al navegador que inició el flujo. */
const COOKIE_ESTADO = 'swmatrc_oauth_state';
const RUTA_CALLBACK = '/api/auth/callback';

function leerCookie(peticion: Request, nombre: string): string | undefined {
  for (const parte of (peticion.header('cookie') ?? '').split(';')) {
    const [clave, ...valor] = parte.trim().split('=');
    if (clave === nombre) return decodeURIComponent(valor.join('='));
  }
  return undefined;
}

/**
 * Inicio de sesión con proveedores externos (RF-OAUTH-01…05). Rutas públicas: el usuario
 * todavía no tiene token. Las URL de callback que se registran en cada proveedor son
 * `https://<dominio>/api/auth/callback/<proveedor>`.
 */
@Controller('api/auth')
@Publico()
export class OauthController {
  private readonly logger = new Logger(OauthController.name);

  constructor(private readonly oauth: OauthService) {}

  /** Proveedores habilitados, para dibujar los botones de la pantalla de acceso. */
  @Get('providers')
  proveedores() {
    return this.oauth.listar();
  }

  /** Redirige al proveedor (Google, GitHub…) para que el usuario se autentique. */
  @Get('login/:proveedor')
  async iniciar(@Param('proveedor') proveedor: string, @Res() respuesta: Response) {
    try {
      const { url, estado } = await this.oauth.iniciar(proveedor);
      respuesta.cookie(COOKIE_ESTADO, estado, {
        httpOnly: true,
        secure: this.oauth.cookieSegura,
        // Lax: el navegador la manda en la redirección de vuelta (navegación GET de nivel superior).
        sameSite: 'lax',
        path: RUTA_CALLBACK,
        maxAge: 10 * 60_000,
      });
      respuesta.redirect(HttpStatus.FOUND, url);
    } catch (error) {
      this.fallo(respuesta, error);
    }
  }

  /** Vuelta desde el proveedor. Termina siempre en el frontend: con un código o con un error. */
  @Get('callback/:proveedor')
  async callback(
    @Param('proveedor') proveedor: string,
    @Query() parametros: { code?: string; state?: string; error?: string },
    @Req() peticion: Request,
    @Res() respuesta: Response,
    @Contexto() ctx: ContextoPeticion,
  ) {
    respuesta.clearCookie(COOKIE_ESTADO, { path: RUTA_CALLBACK });
    try {
      const codigo = await this.oauth.completar(
        proveedor,
        {
          code: typeof parametros.code === 'string' ? parametros.code : undefined,
          state: typeof parametros.state === 'string' ? parametros.state : undefined,
          error: typeof parametros.error === 'string' ? parametros.error : undefined,
        },
        leerCookie(peticion, COOKIE_ESTADO),
        ctx,
      );
      respuesta.redirect(HttpStatus.FOUND, this.oauth.urlRetorno({ codigo, proveedor }));
    } catch (error) {
      this.fallo(respuesta, error, proveedor);
    }
  }

  /** El frontend canjea el código de un solo uso por el JWT del sistema. */
  @Post('exchange')
  @HttpCode(HttpStatus.OK)
  canjear(@Body() dto: CanjearCodigoDto, @Contexto() ctx: ContextoPeticion, @Req() peticion: Request) {
    return this.oauth.canjear(dto.codigo, { ...ctx, agente: peticion.header('user-agent') });
  }

  private fallo(respuesta: Response, error: unknown, proveedor?: string): void {
    const motivo = error instanceof FalloOauth ? error.motivo : 'proveedor-no-disponible';
    if (!(error instanceof FalloOauth)) this.logger.error(`Error inesperado en OAuth: ${(error as Error).stack}`);
    else this.logger.warn(`Inicio de sesión externo rechazado (${motivo}): ${error.message}`);
    respuesta.redirect(HttpStatus.FOUND, this.oauth.urlRetorno({ error: motivo, ...(proveedor ? { proveedor } : {}) }));
  }
}

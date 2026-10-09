import { Body, Controller, Get, HttpCode, HttpStatus, Param, ParseUUIDPipe, Post, Req } from '@nestjs/common';
import type { Request } from 'express';
import {
  Contexto,
  Publico,
  UsuarioActual,
  type ContextoPeticion,
  type Identidad,
} from '../comun/decoradores.js';
import { SoloServicios } from '../seguridad/guarda-autenticacion.js';
import { CambiarPasswordPropiaDto, LoginDto, TokenServicioDto } from '../usuarios/dto.js';
import { UsuariosService } from '../usuarios/usuarios.service.js';
import { AutenticacionService } from './autenticacion.service.js';

/** Acceso al sistema y cuenta propia. */
@Controller('api/auth')
export class AutenticacionController {
  constructor(
    private readonly autenticacion: AutenticacionService,
    private readonly usuarios: UsuariosService,
  ) {}

  @Post('login')
  @Publico()
  @HttpCode(HttpStatus.OK)
  login(@Body() dto: LoginDto, @Contexto() ctx: ContextoPeticion, @Req() peticion: Request) {
    return this.autenticacion.iniciarSesion(dto, { ...ctx, agente: peticion.header('user-agent') });
  }

  @Post('logout')
  @HttpCode(HttpStatus.NO_CONTENT)
  logout(@UsuarioActual() identidad: Identidad, @Contexto() ctx: ContextoPeticion) {
    return this.autenticacion.cerrarSesion(identidad, ctx);
  }

  /** Perfil del usuario del token, leído de la base: refleja cambios hechos después del login. */
  @Get('me')
  yo(@UsuarioActual() identidad: Identidad) {
    return this.usuarios.obtener(identidad.id);
  }

  @Post('me/password')
  @HttpCode(HttpStatus.NO_CONTENT)
  cambiarPassword(
    @UsuarioActual() identidad: Identidad,
    @Body() dto: CambiarPasswordPropiaDto,
    @Contexto() ctx: ContextoPeticion,
  ) {
    return this.autenticacion.cambiarPasswordPropia(identidad, dto.passwordActual, dto.passwordNueva, ctx);
  }
}

/**
 * Rutas para los demás microservicios. Viven fuera de `/api`, así que el gateway no las
 * enruta: solo se alcanzan dentro de la red interna de Docker.
 */
@Controller('internal')
export class InternoController {
  constructor(private readonly autenticacion: AutenticacionService) {}

  @Post('token')
  @Publico()
  @HttpCode(HttpStatus.OK)
  token(@Body() dto: TokenServicioDto) {
    return this.autenticacion.tokenServicio(dto.clienteId, dto.secreto);
  }

  /** Permite a otro servicio confirmar que una sesión no fue cerrada antes de una operación sensible. */
  @Get('sessions/:jti')
  @SoloServicios()
  sesion(@Param('jti', ParseUUIDPipe) jti: string) {
    return this.autenticacion.estadoSesion(jti);
  }
}

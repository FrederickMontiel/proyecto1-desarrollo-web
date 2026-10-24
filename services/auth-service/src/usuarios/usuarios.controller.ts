import {
  Body,
  Controller,
  Get,
  HttpCode,
  HttpStatus,
  Param,
  ParseIntPipe,
  Patch,
  Post,
  Put,
  Query,
} from '@nestjs/common';
import { InjectRepository } from '@nestjs/typeorm';
import { Repository } from 'typeorm';
import { Contexto, RequierePermisos, type ContextoPeticion } from '../comun/decoradores.js';
import { PERMISOS } from './catalogo.js';
import {
  ActualizarUsuarioDto,
  CambiarEstadoDto,
  CrearUsuarioDto,
  FiltroUsuariosDto,
  RestablecerPasswordDto,
} from './dto.js';
import { Rol } from './entidades.js';
import { UsuariosService } from './usuarios.service.js';

/** Administración de cuentas. No hay auto-registro: las altas las hace un administrador. */
@Controller('api/auth')
@RequierePermisos(PERMISOS.usuariosAdministrar.codigo)
export class UsuariosController {
  constructor(
    private readonly usuarios: UsuariosService,
    @InjectRepository(Rol) private readonly roles: Repository<Rol>,
  ) {}

  @Get('users')
  listar(@Query() filtro: FiltroUsuariosDto) {
    return this.usuarios.listar(filtro);
  }

  @Get('users/:id')
  obtener(@Param('id', ParseIntPipe) id: number) {
    return this.usuarios.obtener(id);
  }

  @Post('users')
  crear(@Body() dto: CrearUsuarioDto, @Contexto() ctx: ContextoPeticion) {
    return this.usuarios.crear(dto, ctx);
  }

  @Put('users/:id')
  actualizar(@Param('id', ParseIntPipe) id: number, @Body() dto: ActualizarUsuarioDto, @Contexto() ctx: ContextoPeticion) {
    return this.usuarios.actualizar(id, dto, ctx);
  }

  /** Habilita o deshabilita una cuenta. Las cuentas no se eliminan: la auditoría las referencia. */
  @Patch('users/:id/status')
  cambiarEstado(@Param('id', ParseIntPipe) id: number, @Body() dto: CambiarEstadoDto, @Contexto() ctx: ContextoPeticion) {
    return this.usuarios.cambiarEstado(id, dto.activo, ctx);
  }

  /** Asigna una contraseña nueva sin conocer la anterior: la vía para recuperar el acceso. */
  @Post('users/:id/password')
  @HttpCode(HttpStatus.NO_CONTENT)
  restablecerPassword(
    @Param('id', ParseIntPipe) id: number,
    @Body() dto: RestablecerPasswordDto,
    @Contexto() ctx: ContextoPeticion,
  ) {
    return this.usuarios.restablecerPassword(id, dto.passwordNueva, ctx);
  }

  @Get('roles')
  async listarRoles() {
    const roles = await this.roles.find({ order: { id: 'ASC' } });
    return roles.map((r) => ({
      codigo: r.codigo,
      descripcion: r.descripcion,
      permisos: r.permisos.map((p) => ({ codigo: p.codigo, descripcion: p.descripcion })),
    }));
  }
}

import { BadRequestException, ConflictException, Injectable, NotFoundException } from '@nestjs/common';
import { InjectDataSource } from '@nestjs/typeorm';
import bcrypt from 'bcryptjs';
import { Brackets, DataSource, IsNull, type EntityManager } from 'typeorm';
import { AuditoriaService } from '../auditoria/auditoria.service.js';
import type { ContextoPeticion } from '../comun/decoradores.js';
import { CodigoRol } from './catalogo.js';
import {
  aRespuesta,
  type ActualizarUsuarioDto,
  type CrearUsuarioDto,
  type FiltroUsuariosDto,
  type UsuarioRespuesta,
} from './dto.js';
import { Rol, Sesion, Usuario } from './entidades.js';

/** Mismo factor que usaba BCrypt.Net en la Fase 2: los hashes migrados siguen siendo comparables. */
export const FACTOR_BCRYPT = 12;

@Injectable()
export class UsuariosService {
  constructor(
    @InjectDataSource() private readonly db: DataSource,
    private readonly auditoria: AuditoriaService,
  ) {}

  async listar(filtro: FiltroUsuariosDto): Promise<UsuarioRespuesta[]> {
    const consulta = this.db
      .getRepository(Usuario)
      .createQueryBuilder('u')
      .innerJoinAndSelect('u.rol', 'rol')
      .leftJoinAndSelect('rol.permisos', 'permiso')
      .orderBy('u.nombreCompleto', 'ASC');

    if (filtro.busqueda?.trim()) {
      const texto = `%${filtro.busqueda.trim()}%`;
      consulta.andWhere(
        new Brackets((w) => w.where('u.nombreCompleto ILIKE :texto', { texto }).orWhere('u.email ILIKE :texto', { texto })),
      );
    }
    if (filtro.rol) consulta.andWhere('rol.codigo = :rol', { rol: filtro.rol });
    if (filtro.activo !== undefined) consulta.andWhere('u.activo = :activo', { activo: filtro.activo });

    return (await consulta.getMany()).map(aRespuesta);
  }

  async obtener(id: number): Promise<UsuarioRespuesta> {
    return aRespuesta(await this.cargar(this.db.manager, id));
  }

  async crear(dto: CrearUsuarioDto, ctx: ContextoPeticion): Promise<UsuarioRespuesta> {
    const email = dto.email.toLowerCase();
    const repo = this.db.getRepository(Usuario);

    if (await repo.existsBy({ email }))
      throw new ConflictException(`Ya existe un usuario registrado con el correo '${email}'.`);

    const usuario = await repo.save(
      repo.create({
        nombreCompleto: dto.nombreCompleto,
        email,
        passwordHash: await bcrypt.hash(dto.password, FACTOR_BCRYPT),
        rol: await this.rol(this.db.manager, dto.rol),
        activo: true,
      }),
    );

    this.auditoria.registrar(
      { accion: 'UsuarioCreado', entidad: 'Usuario', entidadId: usuario.id, detalle: { email, rol: dto.rol } },
      ctx,
    );
    return this.obtener(usuario.id);
  }

  async actualizar(id: number, dto: ActualizarUsuarioDto, ctx: ContextoPeticion): Promise<UsuarioRespuesta> {
    return this.db.transaction(async (tx) => {
      const usuario = await this.cargar(tx, id);
      const rolAnterior = usuario.rol.codigo;

      // Quitarse a uno mismo el rol de administrador deja la sesión en curso con un token
      // que ya no corresponde a los permisos reales: se bloquea antes de que ocurra.
      if (usuario.id === ctx.identidad?.id && rolAnterior === CodigoRol.Administrador && dto.rol !== CodigoRol.Administrador)
        throw new BadRequestException('No puede retirarse a sí mismo el rol de administrador.');

      await this.asegurarQueQuedaAdministrador(tx, usuario, dto.rol, usuario.activo);

      usuario.nombreCompleto = dto.nombreCompleto;
      usuario.rol = await this.rol(tx, dto.rol);
      await tx.save(usuario);

      // Los permisos viajan dentro del token: si el rol cambió, las sesiones abiertas
      // tienen permisos viejos y se cierran para obligar a iniciar sesión de nuevo.
      if (rolAnterior !== dto.rol) await this.revocarSesiones(tx, usuario.id);

      this.auditoria.registrar(
        {
          accion: 'UsuarioActualizado',
          entidad: 'Usuario',
          entidadId: usuario.id,
          detalle: { email: usuario.email, rolAnterior, rolNuevo: dto.rol },
        },
        ctx,
      );
      return aRespuesta(await this.cargar(tx, id));
    });
  }

  async cambiarEstado(id: number, activo: boolean, ctx: ContextoPeticion): Promise<UsuarioRespuesta> {
    return this.db.transaction(async (tx) => {
      const usuario = await this.cargar(tx, id);

      if (!activo) {
        if (usuario.id === ctx.identidad?.id) throw new BadRequestException('No puede deshabilitar su propia cuenta.');
        await this.asegurarQueQuedaAdministrador(tx, usuario, usuario.rol.codigo, false);
        await this.revocarSesiones(tx, usuario.id);
      }

      usuario.activo = activo;
      await tx.save(usuario);

      this.auditoria.registrar(
        {
          accion: activo ? 'UsuarioHabilitado' : 'UsuarioDeshabilitado',
          entidad: 'Usuario',
          entidadId: usuario.id,
          detalle: { email: usuario.email },
        },
        ctx,
      );
      return aRespuesta(usuario);
    });
  }

  async restablecerPassword(id: number, passwordNueva: string, ctx: ContextoPeticion): Promise<void> {
    await this.db.transaction(async (tx) => {
      const usuario = await this.cargar(tx, id);
      await tx.update(Usuario, { id }, { passwordHash: await bcrypt.hash(passwordNueva, FACTOR_BCRYPT) });
      // Si la contraseña se restablece porque la cuenta estaba comprometida, las sesiones
      // abiertas con la anterior no deben seguir vivas.
      await this.revocarSesiones(tx, id);

      // La contraseña nunca se registra, ni siquiera hasheada: la bitácora deja constancia
      // del hecho, no del secreto.
      this.auditoria.registrar(
        { accion: 'PasswordRestablecida', entidad: 'Usuario', entidadId: id, detalle: { email: usuario.email } },
        ctx,
      );
    });
  }

  async revocarSesiones(tx: EntityManager, usuarioId: number): Promise<void> {
    await tx.update(Sesion, { usuarioId, revocadaEn: IsNull() }, { revocadaEn: new Date() });
  }

  private async cargar(tx: EntityManager, id: number): Promise<Usuario> {
    const usuario = await tx.findOne(Usuario, { where: { id } });
    if (!usuario) throw new NotFoundException(`No se encontró el usuario con id ${id}.`);
    return usuario;
  }

  private async rol(tx: EntityManager, codigo: CodigoRol): Promise<Rol> {
    return tx.findOneByOrFail(Rol, { codigo });
  }

  /**
   * Impide dejar la instalación sin ningún administrador activo. Sin esta comprobación
   * una sola operación podría cerrar el acceso a la administración de forma irreversible.
   */
  private async asegurarQueQuedaAdministrador(
    tx: EntityManager,
    usuario: Usuario,
    rolFuturo: string,
    activoFuturo: boolean,
  ): Promise<void> {
    if (rolFuturo === CodigoRol.Administrador && activoFuturo) return;
    if (usuario.rol.codigo !== CodigoRol.Administrador || !usuario.activo) return;

    const otros = await tx
      .getRepository(Usuario)
      .createQueryBuilder('u')
      .innerJoin('u.rol', 'rol')
      .where('u.id <> :id', { id: usuario.id })
      .andWhere('rol.codigo = :admin', { admin: CodigoRol.Administrador })
      .andWhere('u.activo = true')
      .getCount();

    if (otros === 0)
      throw new BadRequestException(
        'Es el único administrador activo. Designe otro antes de retirarle el rol o deshabilitarlo.',
      );
  }
}

import { BadRequestException, Injectable, UnauthorizedException } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { InjectDataSource } from '@nestjs/typeorm';
import bcrypt from 'bcryptjs';
import { createHash, timingSafeEqual } from 'node:crypto';
import { DataSource, LessThan } from 'typeorm';
import { AuditoriaService } from '../auditoria/auditoria.service.js';
import type { ContextoPeticion, Identidad } from '../comun/decoradores.js';
import type { Configuracion } from '../configuracion.js';
import { TokensService } from '../seguridad/tokens.service.js';
import { aRespuesta, type LoginDto, type SesionRespuesta } from '../usuarios/dto.js';
import { Sesion, Usuario } from '../usuarios/entidades.js';
import { FACTOR_BCRYPT, UsuariosService } from '../usuarios/usuarios.service.js';

// Hash de una contraseña cualquiera. Cuando el correo no existe se compara contra él para
// que la respuesta tarde lo mismo que con un correo real: el tiempo no revela qué cuentas
// están registradas.
const HASH_SENUELO = bcrypt.hashSync('contraseña-que-nadie-usa', FACTOR_BCRYPT);

@Injectable()
export class AutenticacionService {
  private readonly clientesServicio: Map<string, string>;

  constructor(
    @InjectDataSource() private readonly db: DataSource,
    private readonly tokens: TokensService,
    private readonly usuarios: UsuariosService,
    private readonly auditoria: AuditoriaService,
    config: ConfigService<Configuracion, true>,
  ) {
    this.clientesServicio = config.get('clientesServicio', { infer: true });
  }

  async iniciarSesion(dto: LoginDto, ctx: ContextoPeticion & { agente?: string }): Promise<SesionRespuesta> {
    const email = dto.email.toLowerCase();
    const usuario = await this.db
      .getRepository(Usuario)
      .createQueryBuilder('u')
      .addSelect('u.passwordHash')
      .innerJoinAndSelect('u.rol', 'rol')
      .leftJoinAndSelect('rol.permisos', 'permiso')
      .where('u.email = :email', { email })
      .getOne();

    const valida = await bcrypt.compare(dto.password, usuario?.passwordHash ?? HASH_SENUELO);

    // Mismo mensaje para usuario inexistente y contraseña incorrecta: no se revela cuáles
    // correos están registrados.
    if (!usuario || !valida) {
      this.auditoria.registrar(
        { accion: 'InicioSesionFallido', entidad: 'Usuario', detalle: { email } },
        ctx,
      );
      throw new UnauthorizedException('Credenciales inválidas.');
    }

    if (!usuario.activo) throw new UnauthorizedException('La cuenta está deshabilitada. Contacte al administrador.');

    const respuesta = aRespuesta(usuario);
    const emitido = await this.tokens.emitirUsuario({
      id: usuario.id,
      email: usuario.email,
      nombre: usuario.nombreCompleto,
      rol: respuesta.rol,
      permisos: respuesta.permisos,
    });

    await this.db.transaction(async (tx) => {
      await tx.update(Usuario, { id: usuario.id }, { ultimoAcceso: emitido.emitido });
      await tx.insert(Sesion, {
        jti: emitido.jti,
        usuarioId: usuario.id,
        emitidaEn: emitido.emitido,
        expiraEn: emitido.expira,
        direccionIp: ctx.ip?.slice(0, 64) ?? null,
        agenteUsuario: ctx.agente?.slice(0, 300) ?? null,
      });
      // Limpieza oportunista: las sesiones vencidas de este usuario ya no sirven para nada.
      await tx.delete(Sesion, { usuarioId: usuario.id, expiraEn: LessThan(new Date()) });
    });

    // El contexto de esta petición es anónimo —el token acaba de emitirse—, así que el
    // autor se indica de forma explícita para que la auditoría no diga "sistema".
    this.auditoria.registrar(
      {
        accion: 'InicioSesion',
        entidad: 'Usuario',
        entidadId: usuario.id,
        detalle: { rol: respuesta.rol },
        autor: { id: usuario.id, email: usuario.email },
      },
      ctx,
    );

    return { token: emitido.token, expira: emitido.expira, usuario: { ...respuesta, ultimoAcceso: emitido.emitido } };
  }

  /** Revoca la sesión del token con que se llama: deja de servir aunque no haya vencido. */
  async cerrarSesion(identidad: Identidad, ctx: ContextoPeticion): Promise<void> {
    await this.db.getRepository(Sesion).update({ jti: identidad.jti }, { revocadaEn: new Date() });
    this.auditoria.registrar({ accion: 'CierreSesion', entidad: 'Usuario', entidadId: identidad.id }, ctx);
  }

  async cambiarPasswordPropia(
    identidad: Identidad,
    passwordActual: string,
    passwordNueva: string,
    ctx: ContextoPeticion,
  ): Promise<void> {
    const usuario = await this.db
      .getRepository(Usuario)
      .createQueryBuilder('u')
      .addSelect('u.passwordHash')
      .where('u.id = :id', { id: identidad.id })
      .getOneOrFail();

    if (!(await bcrypt.compare(passwordActual, usuario.passwordHash)))
      throw new BadRequestException('La contraseña actual no es correcta.');

    await this.db.transaction(async (tx) => {
      await tx.update(Usuario, { id: usuario.id }, { passwordHash: await bcrypt.hash(passwordNueva, FACTOR_BCRYPT) });
      // Se cierran las demás sesiones; la actual sigue abierta para no expulsar al usuario.
      await this.usuarios.revocarSesiones(tx, usuario.id);
      await tx.update(Sesion, { jti: identidad.jti }, { revocadaEn: null });
    });

    this.auditoria.registrar({ accion: 'PasswordCambiada', entidad: 'Usuario', entidadId: usuario.id }, ctx);
  }

  /** Intercambia las credenciales de un servicio interno por un token de servicio. */
  async tokenServicio(clienteId: string, secreto: string) {
    const esperado = this.clientesServicio.get(clienteId);
    // Se comparan resúmenes de longitud fija en tiempo constante: ni la longitud ni el
    // contenido del secreto se filtran por el tiempo de respuesta.
    const resumen = (texto: string) => createHash('sha256').update(texto).digest();
    if (!esperado || !timingSafeEqual(resumen(secreto), resumen(esperado)))
      throw new UnauthorizedException('Credenciales de servicio inválidas.');

    const emitido = await this.tokens.emitirServicio(clienteId);
    return { token: emitido.token, expira: emitido.expira };
  }

  async estadoSesion(jti: string) {
    const sesion = await this.db.getRepository(Sesion).findOne({ where: { jti }, relations: { usuario: true } });
    const vigente = !!sesion && !sesion.revocadaEn && sesion.expiraEn > new Date() && sesion.usuario.activo;
    return { jti, vigente };
  }
}

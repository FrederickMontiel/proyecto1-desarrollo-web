import {
  CanActivate,
  ExecutionContext,
  ForbiddenException,
  Injectable,
  SetMetadata,
  UnauthorizedException,
} from '@nestjs/common';
import { Reflector } from '@nestjs/core';
import { InjectRepository } from '@nestjs/typeorm';
import { IsNull, MoreThan, Repository } from 'typeorm';
import { CLAVE_PERMISOS, CLAVE_PUBLICO, CLAVE_ROLES, type PeticionAutenticada } from '../comun/decoradores.js';
import { CodigoRol, PERMISOS_ADMINISTRATIVOS } from '../usuarios/catalogo.js';
import { Sesion } from '../usuarios/entidades.js';
import { PERMISO_INTERNO, TokensService } from './tokens.service.js';

export const CLAVE_SOLO_SERVICIOS = 'soloServicios';

/** La ruta solo admite tokens de servicio (audiencia interna), nunca de usuario. */
export const SoloServicios = () => SetMetadata(CLAVE_SOLO_SERVICIOS, true);

const ROLES_USUARIO = new Set<string>(Object.values(CodigoRol));
const ADMINISTRATIVOS = new Set<string>(PERMISOS_ADMINISTRATIVOS);

/**
 * Guarda global. Toda ruta exige token salvo las marcadas con `@Publico()`.
 *
 * A diferencia de los demás servicios, que solo verifican la firma, aquí además se
 * comprueba que la sesión no esté revocada y que la cuenta siga activa: es lo que hace que
 * el logout y la deshabilitación de un usuario tengan efecto inmediato en este servicio.
 */
@Injectable()
export class GuardaAutenticacion implements CanActivate {
  constructor(
    private readonly reflector: Reflector,
    private readonly tokens: TokensService,
    @InjectRepository(Sesion) private readonly sesiones: Repository<Sesion>,
  ) {}

  async canActivate(contexto: ExecutionContext): Promise<boolean> {
    const objetivos = [contexto.getHandler(), contexto.getClass()];
    if (this.reflector.getAllAndOverride<boolean>(CLAVE_PUBLICO, objetivos)) return true;

    const peticion = contexto.switchToHttp().getRequest<PeticionAutenticada>();
    const token = /^Bearer\s+(.+)$/i.exec(peticion.header('authorization') ?? '')?.[1];
    if (!token) throw new UnauthorizedException('Falta el token de acceso.');

    const soloServicios = this.reflector.getAllAndOverride<boolean>(CLAVE_SOLO_SERVICIOS, objetivos);

    let claims;
    try {
      claims = await this.tokens.verificar(token, soloServicios ? 'interna' : 'web');
    } catch {
      throw new UnauthorizedException('El token no es válido o ya venció.');
    }

    if (soloServicios) {
      if (!claims.permissions?.includes(PERMISO_INTERNO))
        throw new ForbiddenException('La ruta es exclusiva de los servicios internos.');
      return true;
    }

    const sesionVigente = await this.sesiones.exists({
      where: {
        jti: claims.jti,
        revocadaEn: IsNull(),
        expiraEn: MoreThan(new Date()),
        usuario: { activo: true },
      },
      relations: { usuario: true },
    });
    if (!sesionVigente) throw new UnauthorizedException('La sesión fue cerrada o la cuenta está deshabilitada.');

    // Un token de usuario sin un rol conocido no es de este sistema (o se fabricó mal).
    const roles = (claims.roles ?? []).filter((r) => ROLES_USUARIO.has(r));
    if (roles.length === 0) throw new UnauthorizedException('El token no trae un rol válido.');

    peticion.identidad = {
      id: Number(claims.sub),
      email: claims.email ?? '',
      nombre: claims.name ?? '',
      roles,
      permisos: claims.permissions ?? [],
      proveedor: claims.provider ?? 'local',
      jti: claims.jti!,
    };

    const rolesRequeridos = this.reflector.getAllAndOverride<string[]>(CLAVE_ROLES, objetivos) ?? [];
    if (rolesRequeridos.length > 0 && !rolesRequeridos.some((r) => roles.includes(r)))
      throw new ForbiddenException('Su rol no tiene acceso a esta operación.');

    const requeridos = this.reflector.getAllAndOverride<string[]>(CLAVE_PERMISOS, objetivos) ?? [];
    const faltantes = requeridos.filter((p) => !peticion.identidad!.permisos.includes(p));
    if (faltantes.length > 0) throw new ForbiddenException('No tiene permisos para realizar esta operación.');

    // RNF-OAUTH-03: un permiso administrativo exige además el rol ADMIN.
    if (requeridos.some((p) => ADMINISTRATIVOS.has(p)) && !roles.includes(CodigoRol.Admin))
      throw new ForbiddenException('La operación está reservada al rol ADMIN.');

    return true;
  }
}

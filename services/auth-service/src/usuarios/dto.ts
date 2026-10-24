import { Transform } from 'class-transformer';
import {
  IsBoolean,
  IsEmail,
  IsEnum,
  IsNotEmpty,
  IsOptional,
  IsString,
  MaxLength,
  MinLength,
} from 'class-validator';
import { CodigoRol } from './catalogo.js';
import type { Usuario } from './entidades.js';

const recortar = ({ value }: { value: unknown }) => (typeof value === 'string' ? value.trim() : value);
const ROLES_VALIDOS = Object.values(CodigoRol).join(', ');

export class LoginDto {
  @Transform(recortar)
  @IsEmail({}, { message: 'El correo no es válido.' })
  email: string;

  @IsString()
  @MinLength(6, { message: 'La contraseña debe tener al menos 6 caracteres.' })
  password: string;
}

export class CrearUsuarioDto {
  @Transform(recortar)
  @IsNotEmpty({ message: 'El nombre es obligatorio.' })
  @MaxLength(120, { message: 'El nombre admite como máximo 120 caracteres.' })
  nombreCompleto: string;

  @Transform(recortar)
  @IsEmail({}, { message: 'El correo no es válido.' })
  @MaxLength(160, { message: 'El correo admite como máximo 160 caracteres.' })
  email: string;

  @IsString()
  @MinLength(8, { message: 'La contraseña debe tener al menos 8 caracteres.' })
  password: string;

  @IsEnum(CodigoRol, { message: `El rol debe ser uno de: ${ROLES_VALIDOS}.` })
  rol: CodigoRol = CodigoRol.Consulta;
}

/** El correo no se modifica: es la credencial de acceso y la referencia de la auditoría. */
export class ActualizarUsuarioDto {
  @Transform(recortar)
  @IsNotEmpty({ message: 'El nombre es obligatorio.' })
  @MaxLength(120, { message: 'El nombre admite como máximo 120 caracteres.' })
  nombreCompleto: string;

  @IsEnum(CodigoRol, { message: `El rol debe ser uno de: ${ROLES_VALIDOS}.` })
  rol: CodigoRol;
}

export class CambiarEstadoDto {
  @IsBoolean({ message: 'El campo "activo" debe ser verdadero o falso.' })
  activo: boolean;
}

export class RestablecerPasswordDto {
  @IsString()
  @MinLength(8, { message: 'La contraseña debe tener al menos 8 caracteres.' })
  passwordNueva: string;
}

export class CambiarPasswordPropiaDto {
  @IsString()
  @IsNotEmpty({ message: 'Indique su contraseña actual.' })
  passwordActual: string;

  @IsString()
  @MinLength(8, { message: 'La contraseña nueva debe tener al menos 8 caracteres.' })
  passwordNueva: string;
}

export class FiltroUsuariosDto {
  @IsOptional()
  @IsString()
  busqueda?: string;

  @IsOptional()
  @IsEnum(CodigoRol, { message: `El rol debe ser uno de: ${ROLES_VALIDOS}.` })
  rol?: CodigoRol;

  // En la query string todo llega como texto: "false" sería verdadero sin esta conversión.
  @IsOptional()
  @Transform(({ value }) => (value === 'true' ? true : value === 'false' ? false : value))
  @IsBoolean({ message: 'El filtro "activo" debe ser true o false.' })
  activo?: boolean;
}

export class TokenServicioDto {
  @IsString()
  @IsNotEmpty()
  clienteId: string;

  @IsString()
  @IsNotEmpty()
  secreto: string;
}

export interface UsuarioRespuesta {
  id: number;
  nombreCompleto: string;
  email: string;
  rol: string;
  rolNombre: string;
  permisos: string[];
  activo: boolean;
  ultimoAcceso: Date | null;
  fechaCreacion: Date;
}

export interface SesionRespuesta {
  token: string;
  expira: Date;
  usuario: UsuarioRespuesta;
}

export function aRespuesta(u: Usuario): UsuarioRespuesta {
  return {
    id: u.id,
    nombreCompleto: u.nombreCompleto,
    email: u.email,
    rol: u.rol.codigo,
    rolNombre: u.rol.codigo,
    permisos: u.rol.permisos.map((p) => p.codigo).sort(),
    activo: u.activo,
    ultimoAcceso: u.ultimoAcceso,
    fechaCreacion: u.fechaCreacion,
  };
}


import { createParamDecorator, ExecutionContext, SetMetadata } from '@nestjs/common';
import type { Request } from 'express';

/** Identidad extraída del token y adjuntada a la petición por la guarda de autenticación. */
export interface Identidad {
  id: number;
  email: string;
  nombre: string;
  rol: string;
  permisos: string[];
  jti: string;
}

/** Contexto de la petición que se adjunta a cada evento de auditoría. */
export interface ContextoPeticion {
  identidad?: Identidad;
  ip?: string;
  correlacion?: string;
}

export type PeticionAutenticada = Request & { identidad?: Identidad };

export const CLAVE_PUBLICO = 'publico';
export const CLAVE_PERMISOS = 'permisos';

/** La ruta no exige token (login, JWKS, sondas de salud). */
export const Publico = () => SetMetadata(CLAVE_PUBLICO, true);

/** La ruta exige todos los permisos indicados. */
export const RequierePermisos = (...permisos: string[]) => SetMetadata(CLAVE_PERMISOS, permisos);

/** Inyecta en el controlador la identidad del usuario autenticado. */
export const UsuarioActual = createParamDecorator(
  (_dato: unknown, ctx: ExecutionContext): Identidad | undefined =>
    ctx.switchToHttp().getRequest<PeticionAutenticada>().identidad,
);

/** Inyecta identidad, IP y correlación: lo que necesita la auditoría. */
export const Contexto = createParamDecorator(
  (_dato: unknown, ctx: ExecutionContext): ContextoPeticion => {
    const peticion = ctx.switchToHttp().getRequest<PeticionAutenticada>();
    return {
      identidad: peticion.identidad,
      ip: peticion.ip,
      correlacion: peticion.header('x-correlation-id'),
    };
  },
);

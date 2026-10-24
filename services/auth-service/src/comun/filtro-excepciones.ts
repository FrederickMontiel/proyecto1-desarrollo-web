import {
  ArgumentsHost,
  Catch,
  ExceptionFilter,
  HttpException,
  HttpStatus,
  Logger,
} from '@nestjs/common';
import type { Request, Response } from 'express';

const TITULOS: Record<number, string> = {
  400: 'Solicitud inválida',
  401: 'No autenticado',
  403: 'Acceso denegado',
  404: 'Recurso no encontrado',
  409: 'Conflicto',
  429: 'Demasiadas solicitudes',
};

/**
 * Responde todos los errores como ProblemDetails (RFC 9457), el mismo formato que usan el
 * gateway y los servicios .NET, para que el frontend los muestre con un solo manejador.
 * Las excepciones inesperadas se responden como 500 genérico: el detalle queda en el log.
 */
@Catch()
export class FiltroExcepciones implements ExceptionFilter {
  private readonly logger = new Logger(FiltroExcepciones.name);

  catch(excepcion: unknown, host: ArgumentsHost): void {
    const contexto = host.switchToHttp();
    const respuesta = contexto.getResponse<Response>();
    const peticion = contexto.getRequest<Request>();

    let estado = HttpStatus.INTERNAL_SERVER_ERROR;
    let detalle = 'Ocurrió un error inesperado al procesar la solicitud.';

    if (excepcion instanceof HttpException) {
      estado = excepcion.getStatus();
      const cuerpo = excepcion.getResponse();
      const mensaje = typeof cuerpo === 'string' ? cuerpo : (cuerpo as { message?: unknown }).message;
      // ValidationPipe devuelve un arreglo con un mensaje por cada campo inválido.
      detalle = Array.isArray(mensaje)
        ? mensaje.join(' ')
        : typeof mensaje === 'string'
          ? mensaje
          : excepcion.message;
      this.logger.warn(`${estado} en ${peticion.method} ${peticion.path}: ${detalle}`);
    } else {
      this.logger.error(`Error no controlado en ${peticion.method} ${peticion.path}`, excepcion as Error);
    }

    respuesta
      .status(estado)
      .type('application/problem+json')
      .json({
        type: `https://httpstatuses.io/${estado}`,
        title: TITULOS[estado] ?? (estado >= 500 ? 'Error interno' : 'Error'),
        status: estado,
        detail: detalle,
        instance: peticion.path,
      });
  }
}

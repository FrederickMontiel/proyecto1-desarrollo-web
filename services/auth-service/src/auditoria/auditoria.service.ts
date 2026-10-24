import { Injectable, Logger } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import type { ContextoPeticion } from '../comun/decoradores.js';
import type { Configuracion } from '../configuracion.js';
import { TokensService } from '../seguridad/tokens.service.js';

export interface EventoAuditoria {
  accion: string;
  entidad: string;
  entidadId?: string | number;
  detalle?: Record<string, unknown>;
  /** Autor explícito, para cuando la petición todavía es anónima (el login). */
  autor?: { id: number; email: string };
}

const NOMBRE_SERVICIO = 'auth-service';

/**
 * Envía eventos al Audit Service. Es "dispara y olvida": la operación del usuario no
 * espera la respuesta ni falla si Audit está caído. Un login no puede depender de que la
 * bitácora esté disponible.
 */
@Injectable()
export class AuditoriaService {
  private readonly logger = new Logger(AuditoriaService.name);
  private readonly url: string;
  private tokenServicio?: { valor: string; expira: number };

  constructor(
    config: ConfigService<Configuracion, true>,
    private readonly tokens: TokensService,
  ) {
    this.url = `${config.get('urlAuditoria', { infer: true })}/internal/events`;
  }

  registrar(evento: EventoAuditoria, contexto: ContextoPeticion): void {
    void this.enviar(evento, contexto).catch((error: Error) =>
      this.logger.warn(`No se pudo registrar "${evento.accion}" en auditoría: ${error.message}`),
    );
  }

  private async enviar(evento: EventoAuditoria, contexto: ContextoPeticion): Promise<void> {
    const autor = evento.autor ?? contexto.identidad;

    const respuesta = await fetch(this.url, {
      method: 'POST',
      headers: {
        'content-type': 'application/json',
        authorization: `Bearer ${await this.token()}`,
        ...(contexto.correlacion ? { 'x-correlation-id': contexto.correlacion } : {}),
      },
      body: JSON.stringify({
        servicio: NOMBRE_SERVICIO,
        usuarioId: autor?.id ?? null,
        usuarioEmail: autor?.email ?? 'sistema',
        accion: evento.accion,
        entidad: evento.entidad,
        entidadId: evento.entidadId != null ? String(evento.entidadId) : null,
        detalle: evento.detalle ?? null,
        direccionIp: contexto.ip ?? null,
        correlacion: contexto.correlacion ?? null,
        fechaHora: new Date().toISOString(),
      }),
      signal: AbortSignal.timeout(3000),
    });

    if (!respuesta.ok) throw new Error(`Audit respondió ${respuesta.status}`);
  }

  /** Auth es el emisor: firma su propio token de servicio y lo reutiliza hasta que casi vence. */
  private async token(): Promise<string> {
    if (!this.tokenServicio || this.tokenServicio.expira - Date.now() < 60_000) {
      const emitido = await this.tokens.emitirServicio(NOMBRE_SERVICIO);
      this.tokenServicio = { valor: emitido.token, expira: emitido.expira.getTime() };
    }
    return this.tokenServicio.valor;
  }
}

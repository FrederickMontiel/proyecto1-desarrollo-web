import { Controller, Get } from '@nestjs/common';
import { HealthCheck, HealthCheckService, TypeOrmHealthIndicator } from '@nestjs/terminus';
import { Publico } from './comun/decoradores.js';

/**
 * Sondas para Docker y Kubernetes. `live` solo confirma que el proceso responde (si falla,
 * se reinicia el contenedor); `ready` además comprueba PostgreSQL (si falla, el gateway
 * deja de mandarle tráfico pero no lo reinicia).
 */
@Controller('health')
@Publico()
export class SaludController {
  constructor(
    private readonly salud: HealthCheckService,
    private readonly db: TypeOrmHealthIndicator,
  ) {}

  @Get('live')
  vivo() {
    return { status: 'ok' };
  }

  @Get('ready')
  @HealthCheck()
  listo() {
    return this.salud.check([() => this.db.pingCheck('postgres', { timeout: 3000 })]);
  }
}

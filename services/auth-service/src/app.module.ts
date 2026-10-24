import { Module } from '@nestjs/common';
import { ConfigModule, ConfigService } from '@nestjs/config';
import { APP_FILTER, APP_GUARD } from '@nestjs/core';
import { TerminusModule } from '@nestjs/terminus';
import { TypeOrmModule } from '@nestjs/typeorm';
import { AuditoriaService } from './auditoria/auditoria.service.js';
import { AutenticacionController, InternoController } from './autenticacion/autenticacion.controller.js';
import { AutenticacionService } from './autenticacion/autenticacion.service.js';
import { MIGRACIONES } from './base-datos/migraciones.js';
import { SemillaService } from './base-datos/semilla.service.js';
import { FiltroExcepciones } from './comun/filtro-excepciones.js';
import { configuracion, type Configuracion } from './configuracion.js';
import { SaludController } from './salud.controller.js';
import { DescubrimientoController } from './seguridad/descubrimiento.controller.js';
import { GuardaAutenticacion } from './seguridad/guarda-autenticacion.js';
import { LlavesService } from './seguridad/llaves.service.js';
import { TokensService } from './seguridad/tokens.service.js';
import { ENTIDADES, Rol, Sesion } from './usuarios/entidades.js';
import { UsuariosController } from './usuarios/usuarios.controller.js';
import { UsuariosService } from './usuarios/usuarios.service.js';

@Module({
  imports: [
    // Sin archivo .env: toda la configuración llega por variables de entorno, como en
    // Compose y en un ConfigMap de Kubernetes.
    ConfigModule.forRoot({ isGlobal: true, ignoreEnvFile: true, load: [configuracion] }),

    TypeOrmModule.forRootAsync({
      inject: [ConfigService],
      useFactory: (config: ConfigService<Configuracion, true>) => {
        const db = config.get('baseDatos', { infer: true });
        return {
          type: 'postgres',
          host: db.host,
          port: db.puerto,
          database: db.nombre,
          username: db.usuario,
          password: db.password,
          entities: ENTIDADES,
          migrations: MIGRACIONES,
          // El esquema solo cambia por migraciones versionadas, que se aplican al arrancar.
          // Con varias réplicas, TypeORM toma un candado para que solo una las ejecute.
          migrationsRun: true,
          synchronize: false,
          retryAttempts: 10,
          retryDelay: 3000,
        };
      },
    }),
    TypeOrmModule.forFeature([Rol, Sesion]),
    TerminusModule,
  ],
  controllers: [
    AutenticacionController,
    UsuariosController,
    InternoController,
    DescubrimientoController,
    SaludController,
  ],
  providers: [
    LlavesService,
    TokensService,
    AuditoriaService,
    UsuariosService,
    AutenticacionService,
    SemillaService,
    { provide: APP_GUARD, useClass: GuardaAutenticacion },
    { provide: APP_FILTER, useClass: FiltroExcepciones },
  ],
})
export class AppModule {}

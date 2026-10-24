import 'reflect-metadata';
import { BadRequestException, Logger, ValidationPipe } from '@nestjs/common';
import { NestFactory } from '@nestjs/core';
import type { NestExpressApplication } from '@nestjs/platform-express';
import { AppModule } from './app.module.js';

async function iniciar() {
  const app = await NestFactory.create<NestExpressApplication>(AppModule);

  // El servicio siempre está detrás del gateway: la IP real del cliente viene en
  // X-Forwarded-For y es la que se registra en la auditoría.
  app.set('trust proxy', true);
  app.disable('x-powered-by');

  app.useGlobalPipes(
    new ValidationPipe({
      transform: true,
      // Campos que el DTO no declara se rechazan: nadie puede colar un "rol" en el login.
      whitelist: true,
      forbidNonWhitelisted: true,
      // class-validator redacta en inglés las reglas que no traen mensaje propio.
      exceptionFactory: (errores) =>
        new BadRequestException(
          errores.flatMap((e) =>
            Object.entries(e.constraints ?? {}).map(([regla, mensaje]) =>
              regla === 'whitelistValidation'
                ? `El campo "${e.property}" no está permitido.`
                : regla === 'isString'
                  ? `El campo "${e.property}" debe ser texto.`
                  : mensaje,
            ),
          ),
        ),
    }),
  );

  // Kubernetes y Compose detienen el contenedor con SIGTERM: con esto Nest cierra el
  // servidor HTTP y el pool de PostgreSQL antes de salir, sin cortar peticiones a medias.
  app.enableShutdownHooks();

  const puerto = Number(process.env.PORT ?? 3000);
  await app.listen(puerto, '0.0.0.0');
  Logger.log(`Auth Service escuchando en el puerto ${puerto}.`, 'Inicio');
}

await iniciar();

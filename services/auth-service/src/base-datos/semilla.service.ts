import { Injectable, Logger, OnApplicationBootstrap } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { InjectDataSource } from '@nestjs/typeorm';
import bcrypt from 'bcryptjs';
import { DataSource, In } from 'typeorm';
import type { Configuracion } from '../configuracion.js';
import { CodigoRol, PERMISOS, ROLES } from '../usuarios/catalogo.js';
import { Permiso, Rol, Usuario } from '../usuarios/entidades.js';
import { FACTOR_BCRYPT } from '../usuarios/usuarios.service.js';

/**
 * Sincroniza el catálogo de roles y permisos con la base en cada arranque y, si no existe
 * ningún usuario, crea el administrador inicial con las credenciales de las variables de
 * entorno. Con la base ya migrada desde la Fase 2 no se crea nada.
 */
@Injectable()
export class SemillaService implements OnApplicationBootstrap {
  private readonly logger = new Logger(SemillaService.name);

  constructor(
    @InjectDataSource() private readonly db: DataSource,
    private readonly config: ConfigService<Configuracion, true>,
  ) {}

  async onApplicationBootstrap(): Promise<void> {
    // Una transacción: con varias réplicas arrancando a la vez, el índice único hace que
    // solo una inserte y las demás encuentren el catálogo ya creado.
    await this.db.transaction(async (tx) => {
      const repoPermisos = tx.getRepository(Permiso);
      const repoRoles = tx.getRepository(Rol);

      for (const { codigo, descripcion } of Object.values(PERMISOS))
        await repoPermisos.upsert({ codigo, descripcion }, ['codigo']);

      for (const [codigo, definicion] of Object.entries(ROLES)) {
        await repoRoles.upsert({ codigo, descripcion: definicion.descripcion }, ['codigo']);
        const rol = await repoRoles.findOneByOrFail({ codigo });
        rol.permisos = await repoPermisos.findBy({ codigo: In(definicion.permisos) });
        await repoRoles.save(rol);
      }
    });

    await this.crearAdministradorInicial();
  }

  private async crearAdministradorInicial(): Promise<void> {
    const repoUsuarios = this.db.getRepository(Usuario);
    if ((await repoUsuarios.count()) > 0) return;

    const { nombre, email, password } = this.config.get('administradorInicial', { infer: true });
    if (!email || !password) {
      this.logger.warn('No hay usuarios y no se definieron ADMIN_EMAIL / ADMIN_PASSWORD: nadie podrá iniciar sesión.');
      return;
    }

    const rol = await this.db.getRepository(Rol).findOneByOrFail({ codigo: CodigoRol.Administrador });
    await repoUsuarios.save(
      repoUsuarios.create({
        nombreCompleto: nombre,
        email: email.trim().toLowerCase(),
        passwordHash: await bcrypt.hash(password, FACTOR_BCRYPT),
        rol,
        activo: true,
      }),
    );

    this.logger.log(`Administrador inicial creado: ${email}.`);
  }
}

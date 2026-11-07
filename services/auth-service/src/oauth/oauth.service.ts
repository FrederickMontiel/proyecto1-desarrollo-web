import { Injectable, Logger, UnauthorizedException } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { InjectDataSource } from '@nestjs/typeorm';
import { createHash, randomBytes } from 'node:crypto';
import { DataSource, LessThan, type EntityManager } from 'typeorm';
import { AuditoriaService } from '../auditoria/auditoria.service.js';
import { AutenticacionService } from '../autenticacion/autenticacion.service.js';
import type { ContextoPeticion } from '../comun/decoradores.js';
import type { Configuracion } from '../configuracion.js';
import { ROL_PREDETERMINADO_EXTERNO } from '../usuarios/catalogo.js';
import type { SesionRespuesta } from '../usuarios/dto.js';
import { CodigoAcceso, IdentidadExterna, Rol, SolicitudOauth, Usuario } from '../usuarios/entidades.js';
import { crearProveedores, ErrorProveedor, type PerfilExterno, type ProveedorIdentidad } from './proveedores.js';

/** Vigencia de la solicitud: el tiempo que el usuario tiene para elegir cuenta en el proveedor. */
const MINUTOS_SOLICITUD = 10;
/** Vigencia del código que el frontend canjea por el JWT. */
const SEGUNDOS_CODIGO = 60;

/**
 * Motivos por los que un inicio de sesión externo no termina. Viajan al frontend en la URL
 * (`/login/callback?error=…`) y allí se traducen a un mensaje.
 */
export type MotivoFallo =
  | 'proveedor-desconocido'
  | 'solicitud-invalida'
  | 'acceso-cancelado'
  | 'proveedor-no-disponible'
  | 'correo-no-verificado'
  | 'no-registrado'
  | 'cuenta-deshabilitada';

export class FalloOauth extends Error {
  constructor(readonly motivo: MotivoFallo, detalle?: string) {
    super(detalle ?? motivo);
  }
}

const aleatorio = (bytes = 32) => randomBytes(bytes).toString('base64url');
const sha256 = (texto: string) => createHash('sha256').update(texto).digest();

/**
 * OAuth 2.0 / OpenID Connect con proveedores externos, centralizado en el Auth Service.
 *
 *   1. iniciar():   guarda state + PKCE + nonce y devuelve la URL del proveedor.
 *   2. completar(): en el callback, valida state, canjea el código, verifica la identidad,
 *                   crea o vincula el usuario y genera un código de un solo uso.
 *   3. canjear():   el frontend cambia ese código por el JWT del sistema.
 *
 * Los demás microservicios nunca ven a Google ni a GitHub: solo reciben el JWT que emite
 * este servicio y lo validan con su JWKS.
 */
@Injectable()
export class OauthService {
  private readonly logger = new Logger(OauthService.name);
  private readonly proveedores: Map<string, ProveedorIdentidad>;
  private readonly oauth: Configuracion['oauth'];

  constructor(
    @InjectDataSource() private readonly db: DataSource,
    private readonly autenticacion: AutenticacionService,
    private readonly auditoria: AuditoriaService,
    config: ConfigService<Configuracion, true>,
  ) {
    this.oauth = config.get('oauth', { infer: true });
    this.proveedores = crearProveedores(this.oauth.proveedores);
    const activos = [...this.proveedores.keys()];
    this.logger.log(
      activos.length > 0
        ? `Proveedores externos activos: ${activos.join(', ')}.`
        : 'Sin proveedores externos configurados: solo inicio de sesión local.',
    );
  }

  /** Para pruebas: reemplaza los proveedores por dobles. */
  usarProveedores(proveedores: ProveedorIdentidad[]): void {
    this.proveedores.clear();
    for (const p of proveedores) this.proveedores.set(p.id, p);
  }

  listar() {
    return [...this.proveedores.values()].map((p) => ({
      id: p.id,
      nombre: p.nombre,
      url: `/api/auth/login/${p.id}`,
    }));
  }

  urlCallback(proveedor: string): string {
    return `${this.oauth.urlPublica}/api/auth/callback/${proveedor}`;
  }

  /** Dirección del frontend a la que vuelve el navegador al terminar, bien o mal. */
  urlRetorno(parametros: Record<string, string>): string {
    return `${this.oauth.urlPublica}/login/callback?${new URLSearchParams(parametros).toString()}`;
  }

  get cookieSegura(): boolean {
    return this.oauth.urlPublica.startsWith('https://');
  }

  async iniciar(idProveedor: string): Promise<{ url: string; estado: string }> {
    const proveedor = this.proveedor(idProveedor);
    const verificador = aleatorio(48);
    const datos = {
      estado: aleatorio(),
      nonce: aleatorio(),
      verificador,
      desafio: sha256(verificador).toString('base64url'),
      urlCallback: this.urlCallback(proveedor.id),
    };

    let url: string;
    try {
      url = await proveedor.urlAutorizacion(datos);
    } catch (error) {
      this.logger.warn(`No se pudo iniciar sesión con ${proveedor.id}: ${(error as Error).message}`);
      throw new FalloOauth('proveedor-no-disponible');
    }

    const repo = this.db.getRepository(SolicitudOauth);
    // Limpieza oportunista de lo que quedó abandonado (usuarios que nunca volvieron).
    await repo.delete({ expiraEn: LessThan(new Date()) });
    await this.db.getRepository(CodigoAcceso).delete({ expiraEn: LessThan(new Date()) });
    await repo.insert({
      estado: datos.estado,
      proveedor: proveedor.id,
      verificador: datos.verificador,
      nonce: datos.nonce,
      expiraEn: new Date(Date.now() + MINUTOS_SOLICITUD * 60_000),
    });

    return { url, estado: datos.estado };
  }

  /**
   * Procesa el callback del proveedor y devuelve el código de un solo uso para el frontend.
   * @param estadoCookie valor de la cookie puesta en iniciar(): liga el callback al mismo
   *                     navegador que empezó el flujo (evita el CSRF de inicio de sesión).
   */
  async completar(
    idProveedor: string,
    parametros: { code?: string; state?: string; error?: string },
    estadoCookie: string | undefined,
    ctx: ContextoPeticion,
  ): Promise<string> {
    const proveedor = this.proveedor(idProveedor);

    // El usuario pulsó "Cancelar" en la pantalla del proveedor.
    if (parametros.error) throw new FalloOauth('acceso-cancelado', parametros.error);
    if (!parametros.code || !parametros.state || parametros.state !== estadoCookie)
      throw new FalloOauth('solicitud-invalida', 'El state no coincide con la cookie.');

    // Se consume la solicitud: un state solo sirve una vez.
    const borradas = await this.db
      .createQueryBuilder()
      .delete()
      .from(SolicitudOauth)
      .where('estado = :estado AND proveedor = :proveedor AND expira_en > now()', {
        estado: parametros.state,
        proveedor: proveedor.id,
      })
      .returning('*')
      .execute();
    const solicitud = (borradas.raw as { verificador: string; nonce: string }[])[0];
    if (!solicitud) throw new FalloOauth('solicitud-invalida', 'La solicitud no existe, ya se usó o venció.');

    let perfil: PerfilExterno;
    try {
      perfil = await proveedor.obtenerPerfil(parametros.code, {
        nonce: solicitud.nonce,
        verificador: solicitud.verificador,
        urlCallback: this.urlCallback(proveedor.id),
      });
    } catch (error) {
      this.logger.warn(`Fallo al validar la identidad con ${proveedor.id}: ${(error as Error).message}`);
      throw new FalloOauth(error instanceof ErrorProveedor ? 'solicitud-invalida' : 'proveedor-no-disponible');
    }

    const usuario = await this.resolverUsuario(proveedor.id, perfil, ctx);

    const codigo = aleatorio();
    await this.db.getRepository(CodigoAcceso).insert({
      codigoHash: sha256(codigo).toString('hex'),
      usuarioId: usuario.id,
      proveedor: proveedor.id,
      expiraEn: new Date(Date.now() + SEGUNDOS_CODIGO * 1000),
    });
    return codigo;
  }

  /** Canjea el código de un solo uso por el JWT del sistema. */
  async canjear(codigo: string, ctx: ContextoPeticion & { agente?: string }): Promise<SesionRespuesta> {
    const borrados = await this.db
      .createQueryBuilder()
      .delete()
      .from(CodigoAcceso)
      .where('codigo_hash = :hash AND expira_en > now()', { hash: sha256(codigo).toString('hex') })
      .returning('*')
      .execute();
    const fila = (borrados.raw as { usuario_id: number; proveedor: string }[])[0];
    if (!fila) throw new UnauthorizedException('El código de acceso no es válido o ya venció. Inicie sesión de nuevo.');

    const usuario = await this.db.getRepository(Usuario).findOne({ where: { id: fila.usuario_id } });
    if (!usuario?.activo) throw new UnauthorizedException('La cuenta está deshabilitada. Contacte al administrador.');

    return this.autenticacion.emitirSesion(usuario, fila.proveedor, ctx);
  }

  /**
   * Encuentra al usuario de una identidad externa:
   *   1. Identidad ya vinculada (proveedor + sub) → ese usuario.
   *   2. Si no, un usuario con el mismo correo **verificado** → se vincula. Así un usuario
   *      migrado de la Fase 2 entra con Google sin perder su rol.
   *   3. Si no, se crea una cuenta nueva con rol VIEWER (si el registro está abierto).
   */
  private async resolverUsuario(proveedor: string, perfil: PerfilExterno, ctx: ContextoPeticion): Promise<Usuario> {
    const eventos: Parameters<AuditoriaService['registrar']>[0][] = [];

    const usuario = await this.db.transaction(async (tx) => {
      const identidad = await tx.findOne(IdentidadExterna, {
        where: { proveedor, sujeto: perfil.sujeto },
        relations: { usuario: true },
      });

      if (identidad) {
        await tx.update(
          IdentidadExterna,
          { id: identidad.id },
          { email: perfil.email, nombre: perfil.nombre?.slice(0, 160) ?? null, ultimoAcceso: new Date() },
        );
        return identidad.usuario;
      }

      if (!perfil.email || !perfil.emailVerificado)
        throw new FalloOauth('correo-no-verificado', `La cuenta de ${proveedor} no tiene un correo verificado.`);

      let existente = await tx.findOne(Usuario, { where: { email: perfil.email } });
      if (!existente) {
        if (!this.oauth.registroAbierto) throw new FalloOauth('no-registrado');
        existente = await this.crearUsuario(tx, perfil);
        eventos.push({
          accion: 'UsuarioCreado',
          entidad: 'Usuario',
          entidadId: existente.id,
          detalle: { email: existente.email, rol: ROL_PREDETERMINADO_EXTERNO, proveedor },
          autor: { id: existente.id, email: existente.email },
        });
      }

      await tx.insert(IdentidadExterna, {
        usuarioId: existente.id,
        proveedor,
        sujeto: perfil.sujeto,
        email: perfil.email,
        nombre: perfil.nombre?.slice(0, 160) ?? null,
        ultimoAcceso: new Date(),
      });
      eventos.push({
        accion: 'IdentidadExternaVinculada',
        entidad: 'Usuario',
        entidadId: existente.id,
        detalle: { email: existente.email, proveedor },
        autor: { id: existente.id, email: existente.email },
      });
      return existente;
    });

    if (!usuario.activo) throw new FalloOauth('cuenta-deshabilitada');

    if (!usuario.fotoUrl && perfil.fotoUrl)
      await this.db.getRepository(Usuario).update({ id: usuario.id }, { fotoUrl: perfil.fotoUrl.slice(0, 500) });

    for (const evento of eventos) this.auditoria.registrar(evento, ctx);
    return usuario;
  }

  private async crearUsuario(tx: EntityManager, perfil: PerfilExterno): Promise<Usuario> {
    const rol = await tx.findOneByOrFail(Rol, { codigo: ROL_PREDETERMINADO_EXTERNO });
    const repo = tx.getRepository(Usuario);
    return repo.save(
      repo.create({
        nombreCompleto: (perfil.nombre || perfil.email!.split('@')[0]).slice(0, 120),
        email: perfil.email!,
        passwordHash: null,
        fotoUrl: perfil.fotoUrl?.slice(0, 500) ?? null,
        rol,
        activo: true,
      }),
    );
  }

  private proveedor(id: string): ProveedorIdentidad {
    const proveedor = this.proveedores.get(id);
    if (!proveedor) throw new FalloOauth('proveedor-desconocido');
    return proveedor;
  }
}

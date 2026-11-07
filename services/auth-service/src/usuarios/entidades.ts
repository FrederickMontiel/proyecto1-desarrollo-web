import {
  Column,
  CreateDateColumn,
  Entity,
  Index,
  JoinColumn,
  JoinTable,
  ManyToMany,
  ManyToOne,
  PrimaryColumn,
  PrimaryGeneratedColumn,
  type Relation,
  UpdateDateColumn,
} from 'typeorm';

@Entity('permisos')
export class Permiso {
  @PrimaryGeneratedColumn({ type: 'int' })
  id: number;

  @Column({ type: 'varchar', length: 60, unique: true })
  codigo: string;

  @Column({ type: 'varchar', length: 200 })
  descripcion: string;
}

@Entity('roles')
export class Rol {
  @PrimaryGeneratedColumn({ type: 'int' })
  id: number;

  @Column({ type: 'varchar', length: 40, unique: true })
  codigo: string;

  @Column({ type: 'varchar', length: 200 })
  descripcion: string;

  @ManyToMany(() => Permiso, { eager: true })
  @JoinTable({
    name: 'roles_permisos',
    joinColumn: { name: 'rol_id' },
    inverseJoinColumn: { name: 'permiso_id' },
  })
  permisos: Relation<Permiso[]>;
}

@Entity('usuarios')
export class Usuario {
  @PrimaryGeneratedColumn({ type: 'int' })
  id: number;

  @Column({ name: 'nombre_completo', type: 'varchar', length: 120 })
  nombreCompleto: string;

  // Se guarda siempre en minúsculas: el índice único no distingue "Ana@x" de "ana@x".
  @Column({ type: 'varchar', length: 160, unique: true })
  email: string;

  /** Nulo en las cuentas creadas con un proveedor externo: no tienen contraseña local. */
  @Column({ name: 'password_hash', type: 'varchar', length: 100, nullable: true, select: false })
  passwordHash: string | null;

  /** Foto de perfil que entrega el proveedor externo (Google, GitHub). */
  @Column({ name: 'foto_url', type: 'varchar', length: 500, nullable: true })
  fotoUrl: string | null;

  @ManyToOne(() => Rol, { eager: true, nullable: false })
  @JoinColumn({ name: 'rol_id' })
  rol: Relation<Rol>;

  @Column({ type: 'boolean', default: true })
  activo: boolean;

  @Column({ name: 'ultimo_acceso', type: 'timestamptz', nullable: true })
  ultimoAcceso: Date | null;

  @CreateDateColumn({ name: 'fecha_creacion', type: 'timestamptz' })
  fechaCreacion: Date;

  @UpdateDateColumn({ name: 'fecha_modificacion', type: 'timestamptz', nullable: true })
  fechaModificacion: Date | null;
}

/**
 * Un token emitido. Permite cerrar sesión de verdad: el logout marca la sesión como
 * revocada y la guarda rechaza su token aunque todavía no haya vencido.
 */
@Entity('sesiones')
export class Sesion {
  @PrimaryColumn({ type: 'uuid' })
  jti: string;

  @Index()
  @Column({ name: 'usuario_id', type: 'int' })
  usuarioId: number;

  @ManyToOne(() => Usuario, { onDelete: 'CASCADE' })
  @JoinColumn({ name: 'usuario_id' })
  usuario: Relation<Usuario>;

  @Column({ name: 'emitida_en', type: 'timestamptz' })
  emitidaEn: Date;

  @Column({ name: 'expira_en', type: 'timestamptz' })
  expiraEn: Date;

  @Column({ name: 'revocada_en', type: 'timestamptz', nullable: true })
  revocadaEn: Date | null;

  @Column({ name: 'direccion_ip', type: 'varchar', length: 64, nullable: true })
  direccionIp: string | null;

  @Column({ name: 'agente_usuario', type: 'varchar', length: 300, nullable: true })
  agenteUsuario: string | null;

  /** Con qué se inició la sesión: `local`, `google`, `github`… Va en el claim `provider`. */
  @Column({ type: 'varchar', length: 40, default: 'local' })
  proveedor: string;
}

/**
 * Vínculo entre un usuario local y una cuenta de un proveedor externo (Google, GitHub,
 * Microsoft u otro OIDC). Un usuario puede tener varias: entra con cualquiera de ellas.
 */
@Entity('identidades_externas')
@Index(['proveedor', 'sujeto'], { unique: true })
export class IdentidadExterna {
  @PrimaryGeneratedColumn({ type: 'int' })
  id: number;

  @Column({ name: 'usuario_id', type: 'int' })
  usuarioId: number;

  @ManyToOne(() => Usuario, { onDelete: 'CASCADE' })
  @JoinColumn({ name: 'usuario_id' })
  usuario: Relation<Usuario>;

  /** Identificador del proveedor: `google`, `github`, `facebook`… */
  @Column({ type: 'varchar', length: 40 })
  proveedor: string;

  /** Claim `sub` que el proveedor asigna a la cuenta. */
  @Column({ type: 'varchar', length: 200 })
  sujeto: string;

  @Column({ type: 'varchar', length: 160, nullable: true })
  email: string | null;

  @Column({ type: 'varchar', length: 160, nullable: true })
  nombre: string | null;

  @CreateDateColumn({ name: 'fecha_vinculacion', type: 'timestamptz' })
  fechaVinculacion: Date;

  @Column({ name: 'ultimo_acceso', type: 'timestamptz', nullable: true })
  ultimoAcceso: Date | null;
}

/**
 * Inicio de sesión externo en curso: se crea al mandar al usuario al proveedor y se consume
 * (una sola vez) en el callback. Vive en PostgreSQL y no en memoria para que el callback
 * pueda llegar a cualquier réplica del servicio.
 */
@Entity('oauth_solicitudes')
export class SolicitudOauth {
  /** Valor `state` de OAuth 2.0: aleatorio, protege contra CSRF en el callback. */
  @PrimaryColumn({ type: 'varchar', length: 64 })
  estado: string;

  @Column({ type: 'varchar', length: 40 })
  proveedor: string;

  /** `code_verifier` de PKCE (RFC 7636). */
  @Column({ type: 'varchar', length: 128 })
  verificador: string;

  /** `nonce` de OpenID Connect: liga el id_token a esta solicitud. */
  @Column({ type: 'varchar', length: 64 })
  nonce: string;

  @Column({ name: 'expira_en', type: 'timestamptz' })
  expiraEn: Date;
}

/**
 * Código de un solo uso que el callback entrega al frontend en la URL. El frontend lo canjea
 * por el JWT con un POST: así el token nunca aparece en la barra de direcciones, el historial
 * ni los registros del proxy. Se guarda solo su resumen SHA-256.
 */
@Entity('codigos_acceso')
export class CodigoAcceso {
  @PrimaryColumn({ name: 'codigo_hash', type: 'char', length: 64 })
  codigoHash: string;

  @Column({ name: 'usuario_id', type: 'int' })
  usuarioId: number;

  @Column({ type: 'varchar', length: 40 })
  proveedor: string;

  @Column({ name: 'expira_en', type: 'timestamptz' })
  expiraEn: Date;
}

export const ENTIDADES = [Permiso, Rol, Usuario, Sesion, IdentidadExterna, SolicitudOauth, CodigoAcceso];

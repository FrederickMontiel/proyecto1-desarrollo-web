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

  @Column({ name: 'password_hash', type: 'varchar', length: 100, select: false })
  passwordHash: string;

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
}

/**
 * Vínculo entre un usuario local y una cuenta de un proveedor externo (Google, GitHub,
 * Facebook u otro OIDC). La tabla queda creada en la Fase 3 y se llena en la Fase 4.
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

  @CreateDateColumn({ name: 'fecha_vinculacion', type: 'timestamptz' })
  fechaVinculacion: Date;
}

export const ENTIDADES = [Permiso, Rol, Usuario, Sesion, IdentidadExterna];

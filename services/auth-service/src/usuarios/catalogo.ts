/**
 * Catálogo de permisos y su asignación a roles. Es la única fuente de verdad: al arrancar,
 * el servicio sincroniza estas tablas con la base, así que agregar un permiso aquí basta
 * para que aparezca en los tokens.
 *
 * Los demás servicios autorizan por permiso (más fino) o por rol (compatibilidad con la
 * Fase 2). Ambos viajan en el token.
 */
export const PERMISOS = {
  comunidadesLeer: { codigo: 'comunidades:leer', descripcion: 'Consultar comunidades' },
  comunidadesEditar: { codigo: 'comunidades:editar', descripcion: 'Crear, modificar y desactivar comunidades' },
  sensoresLeer: { codigo: 'sensores:leer', descripcion: 'Consultar sensores y lecturas' },
  sensoresEditar: { codigo: 'sensores:editar', descripcion: 'Crear, modificar y eliminar sensores' },
  sensoresOperar: { codigo: 'sensores:operar', descripcion: 'Activar o desactivar sensores y forzar un valor de prueba' },
  lecturasEditar: { codigo: 'lecturas:editar', descripcion: 'Registrar o corregir lecturas manualmente' },
  alertasLeer: { codigo: 'alertas:leer', descripcion: 'Consultar alertas y eventos' },
  alertasAtender: { codigo: 'alertas:atender', descripcion: 'Reconocer, atender y cerrar alertas' },
  reglasEditar: { codigo: 'reglas:editar', descripcion: 'Administrar reglas y umbrales de alerta' },
  reportesGenerar: { codigo: 'reportes:generar', descripcion: 'Generar reportes PDF' },
  reportesDescargar: { codigo: 'reportes:descargar', descripcion: 'Descargar reportes PDF' },
  auditoriaLeer: { codigo: 'auditoria:leer', descripcion: 'Consultar la bitácora de auditoría' },
  usuariosAdministrar: { codigo: 'usuarios:administrar', descripcion: 'Administrar cuentas de usuario' },
  sistemaAdministrar: { codigo: 'sistema:administrar', descripcion: 'Operaciones de mantenimiento del sistema' },
} as const;

export type CodigoPermiso = (typeof PERMISOS)[keyof typeof PERMISOS]['codigo'];

/**
 * Roles de la Fase 4 (sección 15 del documento). Los códigos viajan en el claim `roles` del
 * JWT; en pantalla se muestran con su nombre en español.
 */
export enum CodigoRol {
  Viewer = 'VIEWER',
  Operator = 'OPERATOR',
  Admin = 'ADMIN',
}

/** Rol de los tokens de servicio a servicio. No se asigna nunca a un usuario. */
export const ROL_SERVICIO = 'SERVICE';

export const NOMBRES_ROL: Record<CodigoRol, string> = {
  [CodigoRol.Viewer]: 'Consulta',
  [CodigoRol.Operator]: 'Operador',
  [CodigoRol.Admin]: 'Administrador',
};

/** Rol con el que entra por primera vez quien se registra con Google, GitHub u otro proveedor. */
export const ROL_PREDETERMINADO_EXTERNO = CodigoRol.Viewer;

const P = PERMISOS;

const LECTURA: CodigoPermiso[] = [P.comunidadesLeer.codigo, P.sensoresLeer.codigo, P.alertasLeer.codigo];

/**
 * Permisos que además exigen el rol ADMIN en todos los servicios (RNF-OAUTH-03: rutas
 * administrativas protegidas por roles). Los servicios repiten esta lista.
 */
export const PERMISOS_ADMINISTRATIVOS: CodigoPermiso[] = [
  P.comunidadesEditar.codigo,
  P.sensoresEditar.codigo,
  P.reglasEditar.codigo,
  P.reportesDescargar.codigo,
  P.auditoriaLeer.codigo,
  P.usuariosAdministrar.codigo,
  P.sistemaAdministrar.codigo,
];

export const ROLES: Record<CodigoRol, { descripcion: string; permisos: CodigoPermiso[] }> = {
  [CodigoRol.Viewer]: {
    descripcion: 'Consulta del dashboard, alertas e historial.',
    permisos: LECTURA,
  },
  [CodigoRol.Operator]: {
    descripcion: 'Gestión operativa de sensores, alertas y eventos; genera reportes.',
    permisos: [
      ...LECTURA,
      P.alertasAtender.codigo,
      P.lecturasEditar.codigo,
      P.sensoresOperar.codigo,
      P.reportesGenerar.codigo,
    ],
  },
  [CodigoRol.Admin]: {
    // RF-REP-10: la descarga de reportes es exclusiva de los administradores.
    descripcion: 'Gestión completa de usuarios, comunidades, sensores, alertas, reportes y auditoría.',
    permisos: Object.values(P).map((p) => p.codigo),
  },
};

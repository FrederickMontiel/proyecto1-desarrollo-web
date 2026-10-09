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

/** Los tres roles de la Fase 2, conservados con el mismo nombre para no romper nada. */
export enum CodigoRol {
  Consulta = 'Consulta',
  Operador = 'Operador',
  Administrador = 'Administrador',
}

const P = PERMISOS;

const LECTURA: CodigoPermiso[] = [P.comunidadesLeer.codigo, P.sensoresLeer.codigo, P.alertasLeer.codigo];

export const ROLES: Record<CodigoRol, { descripcion: string; permisos: CodigoPermiso[] }> = {
  [CodigoRol.Consulta]: {
    descripcion: 'Solo lectura del dashboard y del historial.',
    permisos: LECTURA,
  },
  [CodigoRol.Operador]: {
    descripcion: 'Atiende alertas, corrige lecturas y genera reportes.',
    permisos: [
      ...LECTURA,
      P.alertasAtender.codigo,
      P.lecturasEditar.codigo,
      P.sensoresOperar.codigo,
      P.reportesGenerar.codigo,
    ],
  },
  [CodigoRol.Administrador]: {
    // RF-REP-10: la descarga de reportes es exclusiva de los administradores.
    descripcion: 'Control total del sistema.',
    permisos: Object.values(P).map((p) => p.codigo),
  },
};

/**
 * Contratos de datos compartidos con la API. Reflejan los DTO de C# uno a uno; los
 * enumerados viajan como texto porque el backend los serializa con JsonStringEnumConverter.
 */

export type NivelAlerta = 'Verde' | 'Amarillo' | 'Naranja' | 'Rojo';

export type TipoFenomeno =
  | 'Ninguno'
  | 'Inundacion'
  | 'Sequia'
  | 'Tormenta'
  | 'Helada'
  | 'IncendioForestal';

export type TipoSensor = 'Temperatura' | 'Humedad' | 'Viento' | 'Lluvia' | 'NivelRio';

export type EstadoSensor = 'Inactivo' | 'Activo' | 'SinSenal';

export type RolUsuario = 'Consulta' | 'Operador' | 'Administrador';

export interface Usuario {
  id: number;
  nombreCompleto: string;
  email: string;
  rol: RolUsuario;
  rolNombre: string;
  activo: boolean;
  ultimoAcceso: string | null;
}

/** Alta de una cuenta. El correo queda fijado en este momento: es la credencial de acceso. */
export interface RegistroUsuario {
  nombreCompleto: string;
  email: string;
  password: string;
  rol: RolUsuario;
}

/** Cambios admitidos sobre una cuenta existente. El correo no se modifica. */
export interface ActualizarUsuario {
  nombreCompleto: string;
  rol: RolUsuario;
}

export interface Sesion {
  token: string;
  expira: string;
  usuario: Usuario;
}

export interface Comunidad {
  id: number;
  nombre: string;
  municipio: string;
  departamento: string;
  latitud: number;
  longitud: number;
  poblacion: number;
  activa: boolean;
  totalSensores: number;
}

export interface Sensor {
  id: number;
  comunidadId: number;
  comunidadNombre: string;
  codigo: string;
  nombre: string;
  tipo: TipoSensor;
  tipoNombre: string;
  unidadMedida: string;
  estado: EstadoSensor;
  latitud: number;
  longitud: number;
  valorMinimo: number;
  valorMaximo: number;
  variacionMaxima: number;
  valorActual: number;
  ultimaLectura: string | null;
  umbralAmarilloAlto: number | null;
  umbralNaranjaAlto: number | null;
  umbralRojoAlto: number | null;
  umbralAmarilloBajo: number | null;
  umbralNaranjaBajo: number | null;
  umbralRojoBajo: number | null;
  nivelActual: NivelAlerta;
}

export interface Lectura {
  id: number;
  sensorId: number;
  sensorCodigo: string;
  comunidadId: number;
  tipo: TipoSensor;
  valor: number;
  unidadMedida: string;
  nivel: NivelAlerta;
  fechaHora: string;
}

export interface Alerta {
  id: number;
  comunidadId: number;
  comunidadNombre: string;
  sensorId: number | null;
  sensorCodigo: string | null;
  nivel: NivelAlerta;
  nivelNombre: string;
  fenomeno: TipoFenomeno;
  fenomenoNombre: string;
  mensaje: string;
  valorDisparo: number | null;
  fechaHora: string;
  fechaCierre: string | null;
  activa: boolean;
  reconocida: boolean;
  reconocidaPor: string | null;
  fechaReconocimiento: string | null;
}

export interface Evento {
  id: number;
  comunidadId: number;
  comunidadNombre: string;
  fenomeno: TipoFenomeno;
  fenomenoNombre: string;
  nivelMaximo: NivelAlerta;
  nivelNombre: string;
  descripcion: string;
  origenSensor: string;
  valorRegistrado: number | null;
  fechaInicio: string;
  fechaFin: string | null;
  duracionMinutos: number | null;
}

export interface EstadoComunidad {
  comunidadId: number;
  comunidadNombre: string;
  nivelGlobal: NivelAlerta;
  nivelGlobalNombre: string;
  sensores: Sensor[];
  alertasActivas: Alerta[];
  marca: string;
}

export interface PuntoSerie {
  fechaHora: string;
  valor: number;
}

export interface SerieHistorica {
  sensorId: number;
  sensorCodigo: string;
  sensorNombre: string;
  tipo: TipoSensor;
  unidadMedida: string;
  puntos: PuntoSerie[];
}

export interface ResumenDashboard {
  totalSensores: number;
  sensoresActivos: number;
  sensoresSinSenal: number;
  alertasActivas: number;
  eventosUltimas24h: number;
  nivelGlobal: NivelAlerta;
  alertasPorNivel: Record<string, number>;
  eventosPorFenomeno: Record<string, number>;
}

export interface RegistroBitacora {
  id: number;
  usuarioEmail: string;
  accion: string;
  entidad: string;
  entidadId: string | null;
  detalle: string | null;
  direccionIp: string | null;
  fechaHora: string;
}

export interface Pagina<T> {
  elementos: T[];
  pagina: number;
  tamanoPagina: number;
  totalElementos: number;
  totalPaginas: number;
}

export interface PlantillaSensor {
  tipo: TipoSensor;
  tipoValor: number;
  unidad: string;
  valorMinimo: number;
  valorMaximo: number;
  valorReposo: number;
  variacionMaxima: number;
  umbralAmarilloAlto: number | null;
  umbralNaranjaAlto: number | null;
  umbralRojoAlto: number | null;
  umbralAmarilloBajo: number | null;
  umbralNaranjaBajo: number | null;
  umbralRojoBajo: number | null;
}

export interface CrearSensor {
  comunidadId: number;
  codigo: string;
  nombre: string;
  tipo: number;
  unidadMedida?: string;
  latitud?: number;
  longitud?: number;
  valorMinimo?: number;
  valorMaximo?: number;
  variacionMaxima?: number;
  valorInicial?: number;
  umbralAmarilloAlto?: number | null;
  umbralNaranjaAlto?: number | null;
  umbralRojoAlto?: number | null;
  umbralAmarilloBajo?: number | null;
  umbralNaranjaBajo?: number | null;
  umbralRojoBajo?: number | null;
}

export type ActualizarSensor = Partial<Omit<CrearSensor, 'comunidadId' | 'codigo' | 'tipo'>>;

/** Metadatos de presentación de cada nivel: color, etiqueta y urgencia del aviso sonoro. */
export const NIVELES: Record<NivelAlerta, { etiqueta: string; color: string; orden: number }> = {
  Verde: { etiqueta: 'Normal', color: 'var(--verde)', orden: 0 },
  Amarillo: { etiqueta: 'Precaución', color: 'var(--amarillo)', orden: 1 },
  Naranja: { etiqueta: 'Alerta', color: 'var(--naranja)', orden: 2 },
  Rojo: { etiqueta: 'Emergencia', color: 'var(--rojo)', orden: 3 },
};

export const FENOMENOS: Record<TipoFenomeno, { etiqueta: string; icono: string }> = {
  Ninguno: { etiqueta: 'Sin fenómeno', icono: '—' },
  Inundacion: { etiqueta: 'Inundación', icono: '🌊' },
  Sequia: { etiqueta: 'Sequía', icono: '🏜️' },
  Tormenta: { etiqueta: 'Tormenta', icono: '⛈️' },
  Helada: { etiqueta: 'Helada', icono: '❄️' },
  IncendioForestal: { etiqueta: 'Incendio forestal', icono: '🔥' },
};

/** Descripción de cada rol, para la pantalla de administración de cuentas. */
export const ROLES: Record<RolUsuario, { etiqueta: string; descripcion: string }> = {
  Consulta: {
    etiqueta: 'Consulta',
    descripcion: 'Solo lectura del tablero y del historial.',
  },
  Operador: {
    etiqueta: 'Operador',
    descripcion: 'Reconoce alertas, activa sensores y fija valores manuales.',
  },
  Administrador: {
    etiqueta: 'Administrador',
    descripcion: 'Control total: sensores, cuentas, bitácora y reinicio del sistema.',
  },
};

export const SENSORES: Record<TipoSensor, { etiqueta: string; icono: string }> = {
  Temperatura: { etiqueta: 'Temperatura', icono: '🌡️' },
  Humedad: { etiqueta: 'Humedad relativa', icono: '💧' },
  Viento: { etiqueta: 'Velocidad del viento', icono: '💨' },
  Lluvia: { etiqueta: 'Nivel de lluvia', icono: '🌧️' },
  NivelRio: { etiqueta: 'Nivel del río', icono: '🏞️' },
};

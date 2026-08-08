namespace SWMatrc.Domain.Enums;

/// <summary>Magnitud física que mide un sensor de la red de monitoreo.</summary>
public enum TipoSensor
{
    Temperatura = 1,
    Humedad = 2,
    Viento = 3,
    Lluvia = 4,
    NivelRio = 5
}

/// <summary>
/// Código de color del protocolo de alerta temprana (escala OMM / SINAPRED).
/// El orden es significativo: a mayor valor, mayor severidad.
/// </summary>
public enum NivelAlerta
{
    Verde = 0,
    Amarillo = 1,
    Naranja = 2,
    Rojo = 3
}

/// <summary>Fenómeno hidrometeorológico identificado por el motor de riesgo.</summary>
public enum TipoFenomeno
{
    Ninguno = 0,
    Inundacion = 1,
    Sequia = 2,
    Tormenta = 3,
    Helada = 4,
    IncendioForestal = 5
}

/// <summary>Rol de usuario. Define qué operaciones puede ejecutar sobre el sistema.</summary>
public enum RolUsuario
{
    /// <summary>Solo lectura del dashboard y del historial.</summary>
    Consulta = 0,
    /// <summary>Puede reconocer alertas y editar lecturas de sensores.</summary>
    Operador = 1,
    /// <summary>Control total: sensores, comunidades, usuarios y reinicio del sistema.</summary>
    Administrador = 2
}

/// <summary>Estado operativo de un sensor dentro de la red.</summary>
public enum EstadoSensor
{
    Inactivo = 0,
    Activo = 1,
    /// <summary>El sensor está activo pero no reporta lecturas dentro del umbral de tiempo esperado.</summary>
    SinSenal = 2
}

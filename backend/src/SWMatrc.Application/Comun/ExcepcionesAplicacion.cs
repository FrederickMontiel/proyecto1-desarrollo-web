namespace SWMatrc.Application.Comun;

/// <summary>Error esperado de negocio. El middleware lo traduce a una respuesta HTTP con significado.</summary>
public class ExcepcionAplicacion(string mensaje) : Exception(mensaje);

/// <summary>El recurso solicitado no existe. Se responde 404.</summary>
public sealed class ExcepcionNoEncontrado(string entidad, object id)
    : ExcepcionAplicacion($"No se encontró {entidad} con identificador '{id}'.");

/// <summary>Los datos recibidos no cumplen las reglas de negocio. Se responde 400.</summary>
public sealed class ExcepcionValidacion(string mensaje) : ExcepcionAplicacion(mensaje);

/// <summary>Credenciales inválidas o cuenta deshabilitada. Se responde 401.</summary>
public sealed class ExcepcionAutenticacion(string mensaje) : ExcepcionAplicacion(mensaje);

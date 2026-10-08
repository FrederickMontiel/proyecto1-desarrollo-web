using System.ComponentModel.DataAnnotations;
using SWMatrc.Domain.Entities;
using SWMatrc.Domain.Enums;

namespace SWMatrc.Application.Dtos;

public record LoginRequest
{
    [Required, EmailAddress] public string Email { get; init; } = string.Empty;
    [Required, MinLength(6)] public string Password { get; init; } = string.Empty;
}

public record RegistroRequest
{
    [Required, MaxLength(120)] public string NombreCompleto { get; init; } = string.Empty;
    [Required, EmailAddress, MaxLength(160)] public string Email { get; init; } = string.Empty;
    [Required, MinLength(8)] public string Password { get; init; } = string.Empty;
    public RolUsuario Rol { get; init; } = RolUsuario.Consulta;
}

/// <summary>Cambios admitidos sobre una cuenta existente. El correo no se modifica: es la credencial de acceso y la referencia de la bitácora.</summary>
public record ActualizarUsuarioRequest
{
    [Required, MaxLength(120)] public string NombreCompleto { get; init; } = string.Empty;
    [Required] public RolUsuario Rol { get; init; }
}

/// <summary>Restablecimiento de contraseña ejecutado por un administrador.</summary>
public record RestablecerPasswordRequest
{
    [Required, MinLength(8)] public string PasswordNueva { get; init; } = string.Empty;
}

public record SesionDto
{
    public string Token { get; init; } = string.Empty;
    public DateTime Expira { get; init; }
    public UsuarioDto Usuario { get; init; } = new();
}

public record UsuarioDto
{
    public int Id { get; init; }
    public string NombreCompleto { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public RolUsuario Rol { get; init; }
    public string RolNombre { get; init; } = string.Empty;
    public bool Activo { get; init; }
    public DateTime? UltimoAcceso { get; init; }
    public DateTime FechaCreacion { get; init; }

    public static UsuarioDto Desde(Usuario u) => new()
    {
        Id = u.Id,
        NombreCompleto = u.NombreCompleto,
        Email = u.Email,
        Rol = u.Rol,
        RolNombre = u.Rol.ToString(),
        Activo = u.Activo,
        UltimoAcceso = u.UltimoAcceso,
        FechaCreacion = u.FechaCreacion
    };
}

public record BitacoraDto
{
    public long Id { get; init; }
    public string UsuarioEmail { get; init; } = string.Empty;
    public string Accion { get; init; } = string.Empty;
    public string Entidad { get; init; } = string.Empty;
    public string? EntidadId { get; init; }
    public string? Detalle { get; init; }
    public string? DireccionIp { get; init; }
    public DateTime FechaHora { get; init; }

    public static BitacoraDto Desde(Bitacora b) => new()
    {
        Id = b.Id,
        UsuarioEmail = b.UsuarioEmail,
        Accion = b.Accion,
        Entidad = b.Entidad,
        EntidadId = b.EntidadId,
        Detalle = b.Detalle,
        DireccionIp = b.DireccionIp,
        FechaHora = b.FechaHora
    };
}

public record ComunidadDto
{
    public int Id { get; init; }
    public string Nombre { get; init; } = string.Empty;
    public string Municipio { get; init; } = string.Empty;
    public string Departamento { get; init; } = string.Empty;
    public string Pais { get; init; } = string.Empty;
    public string? Descripcion { get; init; }
    public decimal Latitud { get; init; }
    public decimal Longitud { get; init; }
    public int Poblacion { get; init; }
    public bool Activa { get; init; }
    public int TotalSensores { get; init; }
    public int SensoresActivos { get; init; }
    public DateTime FechaCreacion { get; init; }

    public static ComunidadDto Desde(Comunidad c) => new()
    {
        Id = c.Id,
        Nombre = c.Nombre,
        Municipio = c.Municipio,
        Departamento = c.Departamento,
        Pais = c.Pais,
        Descripcion = c.Descripcion,
        Latitud = c.Latitud,
        Longitud = c.Longitud,
        Poblacion = c.Poblacion,
        Activa = c.Activa,
        TotalSensores = c.Sensores.Count,
        SensoresActivos = c.Sensores.Count(s => s.Estado == EstadoSensor.Activo),
        FechaCreacion = c.FechaCreacion
    };
}

/// <summary>Envoltorio de paginación usado por los listados de historial y bitácora.</summary>
public record PaginaDto<T>
{
    public IReadOnlyList<T> Elementos { get; init; } = [];
    public int Pagina { get; init; }
    public int TamanoPagina { get; init; }
    public int TotalElementos { get; init; }
    public int TotalPaginas => TamanoPagina == 0 ? 0 : (int)Math.Ceiling(TotalElementos / (double)TamanoPagina);
}

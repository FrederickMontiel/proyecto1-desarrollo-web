using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Dtos;

namespace SWMatrc.Api.Controllers;

/// <summary>Acceso al sistema y gestión de cuentas.</summary>
[ApiController]
[Route("api/cuenta")]
public sealed class CuentaController(IServicioAutenticacion autenticacion, IUsuarioActual usuarioActual)
    : ControllerBase
{
    /// <summary>Valida credenciales y entrega el token de acceso.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<SesionDto>> Login(LoginRequest request, CancellationToken ct) =>
        Ok(await autenticacion.IniciarSesionAsync(request, ct));

    /// <summary>Devuelve la identidad asociada al token con el que se llama.</summary>
    [HttpGet("yo")]
    [Authorize]
    public ActionResult<object> Yo() => Ok(new
    {
        id = usuarioActual.Id,
        email = usuarioActual.Email,
        rol = usuarioActual.Rol?.ToString()
    });

    /// <summary>Alta de usuarios. Reservada al administrador: no hay auto-registro abierto.</summary>
    [HttpPost("usuarios")]
    [Authorize(Policy = "Administracion")]
    public async Task<ActionResult<UsuarioDto>> Registrar(RegistroRequest request, CancellationToken ct)
    {
        var usuario = await autenticacion.RegistrarAsync(request, ct);
        return CreatedAtAction(nameof(ListarUsuarios), new { }, usuario);
    }

    [HttpGet("usuarios")]
    [Authorize(Policy = "Administracion")]
    public async Task<ActionResult<IReadOnlyList<UsuarioDto>>> ListarUsuarios(CancellationToken ct) =>
        Ok(await autenticacion.ListarUsuariosAsync(ct));

    /// <summary>Habilita o deshabilita una cuenta. Las cuentas no se eliminan.</summary>
    [HttpPatch("usuarios/{id:int}/estado")]
    [Authorize(Policy = "Administracion")]
    public async Task<ActionResult<UsuarioDto>> CambiarEstado(int id, [FromQuery] bool activo, CancellationToken ct) =>
        Ok(await autenticacion.CambiarEstadoAsync(id, activo, ct));

    /// <summary>Cambia el nombre y el rol de una cuenta. El correo es inmutable.</summary>
    [HttpPut("usuarios/{id:int}")]
    [Authorize(Policy = "Administracion")]
    public async Task<ActionResult<UsuarioDto>> Actualizar(
        int id, ActualizarUsuarioRequest request, CancellationToken ct) =>
        Ok(await autenticacion.ActualizarAsync(id, request, ct));

    /// <summary>
    /// Asigna una contraseña nueva a una cuenta. Es la vía para recuperar el acceso de un
    /// usuario que olvidó la suya, sin que nadie tenga que conocer la anterior.
    /// </summary>
    [HttpPost("usuarios/{id:int}/password")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> RestablecerPassword(
        int id, RestablecerPasswordRequest request, CancellationToken ct)
    {
        await autenticacion.RestablecerPasswordAsync(id, request.PasswordNueva, ct);
        return NoContent();
    }
}

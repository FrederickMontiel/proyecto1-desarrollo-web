using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SWMatrc.Sensores.Iot;

/// <summary>
/// Entrada de los dispositivos físicos. No usa JWT de usuario: un sensor en el campo no inicia
/// sesión. Se autentica con la llave de su dispositivo en <c>X-Api-Key</c> y solo puede
/// reportar los sensores que tiene asignados.
/// </summary>
[ApiController]
[Route("api/sensors/iot")]
[AllowAnonymous]
public sealed class IotController(RegistroIot registro, ServicioIot iot) : ControllerBase
{
    public const string CabeceraLlave = "X-Api-Key";

    /// <summary>
    /// Recibe un lote de mediciones:
    /// <c>POST /api/sensors/iot/readings</c> con <c>{ "lecturas": [ { "codigo": "RPI-TEMP-01", "valor": 24.0 } ] }</c>.
    /// Responde 202: las lecturas se procesan en el siguiente ciclo (cada pocos segundos).
    /// </summary>
    [HttpPost("readings")]
    public async Task<IActionResult> Recibir(LoteIotRequest lote, CancellationToken ct)
    {
        var dispositivo = registro.Autenticar(Request.Headers[CabeceraLlave].FirstOrDefault());
        if (dispositivo is null)
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Dispositivo no autenticado",
                detail: $"Falta la cabecera {CabeceraLlave} o la llave no corresponde a ningún dispositivo.");

        return Accepted(await iot.RecibirAsync(dispositivo, lote, ct));
    }
}

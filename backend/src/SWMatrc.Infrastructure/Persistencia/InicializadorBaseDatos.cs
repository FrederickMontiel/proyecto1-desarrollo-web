using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Domain.Entities;
using SWMatrc.Domain.Enums;
using SWMatrc.Domain.Riesgo;

namespace SWMatrc.Infrastructure.Persistencia;

/// <summary>
/// Prepara la base de datos al arrancar el contenedor: aplica las migraciones pendientes
/// y, si el esquema está vacío, carga el juego de datos mínimo para que el sistema sea
/// utilizable desde el primer inicio de sesión.
/// </summary>
public sealed class InicializadorBaseDatos(
    SwmatrcDbContext db,
    IHasheadorPassword hasheador,
    ILogger<InicializadorBaseDatos> logger)
{
    /// <summary>Reintentos de conexión mientras SQL Server termina de levantar dentro de su contenedor.</summary>
    private const int IntentosConexion = 12;
    private static readonly TimeSpan EsperaEntreIntentos = TimeSpan.FromSeconds(5);

    public async Task InicializarAsync(CancellationToken ct = default)
    {
        await MigrarConReintentosAsync(ct);
        await SembrarAsync(ct);
    }

    private async Task MigrarConReintentosAsync(CancellationToken ct)
    {
        for (var intento = 1; intento <= IntentosConexion; intento++)
        {
            try
            {
                await db.Database.MigrateAsync(ct);
                logger.LogInformation("Migraciones aplicadas correctamente.");
                return;
            }
            catch (Exception ex) when (intento < IntentosConexion)
            {
                // En Docker la API suele estar lista antes que el motor de base de datos;
                // el healthcheck ayuda, pero el reintento evita depender solo de él.
                logger.LogWarning("La base de datos aún no responde (intento {Intento}/{Total}): {Mensaje}",
                    intento, IntentosConexion, ex.Message);
                await Task.Delay(EsperaEntreIntentos, ct);
            }
        }

        // Último intento sin red de seguridad: si falla, el arranque debe fallar con ruido.
        await db.Database.MigrateAsync(ct);
    }

    private async Task SembrarAsync(CancellationToken ct)
    {
        // Las reglas se siembran aparte: una base creada antes de existir las reglas
        // configurables también debe recibir el juego inicial.
        await SembrarReglasAsync(ct);

        if (await db.Comunidades.AnyAsync(ct))
        {
            logger.LogInformation("La base ya contiene datos; se omite la carga inicial.");
            return;
        }

        logger.LogInformation("Base vacía: cargando datos iniciales.");

        await SembrarUsuariosAsync(ct);
        await SembrarComunidadesAsync(ct);

        logger.LogInformation("Carga inicial completada.");
    }

    private async Task SembrarUsuariosAsync(CancellationToken ct)
    {
        if (await db.Usuarios.AnyAsync(ct))
            return;

        // Contraseñas de demostración. En un despliegue real se fuerza el cambio en el
        // primer acceso; aquí quedan documentadas en el README para poder evaluar el sistema.
        db.Usuarios.AddRange(
            new Usuario
            {
                NombreCompleto = "Administrador del Sistema",
                Email = "admin@swmatrc.org",
                PasswordHash = hasheador.Hashear("Admin.2026"),
                Rol = RolUsuario.Administrador
            },
            new Usuario
            {
                NombreCompleto = "Operador de Turno",
                Email = "operador@swmatrc.org",
                PasswordHash = hasheador.Hashear("Operador.2026"),
                Rol = RolUsuario.Operador
            },
            new Usuario
            {
                NombreCompleto = "Comité Comunitario",
                Email = "consulta@swmatrc.org",
                PasswordHash = hasheador.Hashear("Consulta.2026"),
                Rol = RolUsuario.Consulta
            });

        await db.SaveChangesAsync(ct);
    }

    private async Task SembrarComunidadesAsync(CancellationToken ct)
    {
        var comunidades = new[]
        {
            new Comunidad
            {
                Nombre = "San Miguel del Río",
                Municipio = "Jinotega",
                Departamento = "Jinotega",
                Pais = "Nicaragua",
                Descripcion = "Comunidad ribereña expuesta a crecidas del río en temporada de lluvias.",
                Latitud = 13.0884m,
                Longitud = -85.9994m,
                Poblacion = 1840
            },
            new Comunidad
            {
                Nombre = "Valle Verde",
                Municipio = "Matagalpa",
                Departamento = "Matagalpa",
                Pais = "Nicaragua",
                Descripcion = "Comunidad agrícola de ladera con riesgo de sequía e incendios forestales.",
                Latitud = 12.9271m,
                Longitud = -85.9175m,
                Poblacion = 960
            }
        };

        db.Comunidades.AddRange(comunidades);
        await db.SaveChangesAsync(ct);

        var indice = 1;
        foreach (var comunidad in comunidades)
        {
            // Cada comunidad estrena la red completa: las cinco magnitudes que exige el
            // protocolo de monitoreo, calibradas con la plantilla de su tipo.
            foreach (var plantilla in PlantillaSensor.Todas)
            {
                db.Sensores.Add(CrearSensor(comunidad, plantilla, indice));
                indice++;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static Sensor CrearSensor(Comunidad comunidad, PlantillaSensor plantilla, int indice)
    {
        var (nombre, prefijo) = plantilla.Tipo switch
        {
            TipoSensor.Temperatura => ("Termómetro ambiental", "TEMP"),
            TipoSensor.Humedad => ("Higrómetro", "HUM"),
            TipoSensor.Viento => ("Anemómetro", "VNT"),
            TipoSensor.Lluvia => ("Pluviómetro", "LLU"),
            TipoSensor.NivelRio => ("Limnímetro del cauce", "RIO"),
            TipoSensor.NivelReservorio => ("Medidor del reservorio", "RES"),
            TipoSensor.Humo => ("Detector de humo", "HUM-F"),
            _ => ("Sensor", "SEN")
        };

        // Se dispersan los sensores alrededor del casco urbano para que el mapa de la
        // comunidad no los dibuje todos apilados en el mismo punto.
        var desplazamiento = (decimal)((indice % 5) * 0.004 - 0.008);

        return new Sensor
        {
            ComunidadId = comunidad.Id,
            Codigo = $"{prefijo}-{indice:00}",
            Nombre = $"{nombre} — {comunidad.Nombre}",
            Tipo = plantilla.Tipo,
            UnidadMedida = plantilla.Unidad,
            Estado = EstadoSensor.Activo,
            Ubicacion = $"Casco urbano de {comunidad.Nombre}",
            FechaInstalacion = DateTime.UtcNow.Date,
            Latitud = comunidad.Latitud + desplazamiento,
            Longitud = comunidad.Longitud - desplazamiento,
            ValorMinimo = plantilla.ValorMinimo,
            ValorMaximo = plantilla.ValorMaximo,
            VariacionMaxima = plantilla.VariacionMaxima,
            ValorActual = plantilla.ValorReposo,
            UltimaLectura = DateTime.UtcNow,
            UmbralAmarilloAlto = plantilla.AmarilloAlto,
            UmbralNaranjaAlto = plantilla.NaranjaAlto,
            UmbralRojoAlto = plantilla.RojoAlto,
            UmbralAmarilloBajo = plantilla.AmarilloBajo,
            UmbralNaranjaBajo = plantilla.NaranjaBajo,
            UmbralRojoBajo = plantilla.RojoBajo
        };
    }

    private async Task SembrarReglasAsync(CancellationToken ct)
    {
        if (await db.ReglasAlerta.AnyAsync(ct))
            return;

        // Reglas de partida para las magnitudes que el motor integrado no cubre. El
        // administrador puede editarlas, desactivarlas o crear otras desde el panel.
        db.ReglasAlerta.AddRange(
            Regla("Reservorio cerca del rebalse", TipoSensor.NivelReservorio, 92m, 97m, NivelAlerta.Naranja,
                TipoFenomeno.Inundacion, "Alerta: el reservorio {sensor} está al {valor} {unidad}. Preparar el desfogue controlado."),
            Regla("Reservorio en rebalse", TipoSensor.NivelReservorio, 97m, null, NivelAlerta.Rojo,
                TipoFenomeno.Inundacion, "EMERGENCIA: el reservorio {sensor} rebasa el {valor} {unidad}. Evacuar las zonas aguas abajo."),
            Regla("Reservorio en nivel crítico", TipoSensor.NivelReservorio, null, 20m, NivelAlerta.Naranja,
                TipoFenomeno.Sequia, "Alerta de desabastecimiento: el reservorio {sensor} bajó al {valor} {unidad}. Racionar el agua."),
            Regla("Presencia de humo", TipoSensor.Humo, 55m, 150m, NivelAlerta.Amarillo,
                TipoFenomeno.IncendioForestal, "Precaución: {valor} {unidad} de partículas en {comunidad}. Verificar posibles quemas."),
            Regla("Humo denso", TipoSensor.Humo, 150m, 250m, NivelAlerta.Naranja,
                TipoFenomeno.IncendioForestal, "Alerta de incendio: {valor} {unidad} de partículas en {comunidad}. Avisar a la brigada."),
            Regla("Incendio declarado", TipoSensor.Humo, 250m, null, NivelAlerta.Rojo,
                TipoFenomeno.IncendioForestal, "EMERGENCIA por incendio: {valor} {unidad} de partículas. Evacuar y llamar a bomberos."));

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Reglas de alerta iniciales cargadas.");
    }

    private static ReglaAlerta Regla(
        string nombre, TipoSensor tipo, decimal? minimo, decimal? maximo,
        NivelAlerta nivel, TipoFenomeno fenomeno, string mensaje) => new()
    {
        Nombre = nombre,
        TipoSensor = tipo,
        ValorMinimo = minimo,
        ValorMaximo = maximo,
        Nivel = nivel,
        Fenomeno = fenomeno,
        Mensaje = mensaje,
        Activa = true
    };
}

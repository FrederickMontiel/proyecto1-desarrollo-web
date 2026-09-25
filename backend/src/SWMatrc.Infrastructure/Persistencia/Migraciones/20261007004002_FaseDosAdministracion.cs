using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SWMatrc.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class FaseDosAdministracion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Descripcion",
                table: "Sensores",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaInstalacion",
                table: "Sensores",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ubicacion",
                table: "Sensores",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EstadoSensor",
                table: "Lecturas",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "UnidadMedida",
                table: "Lecturas",
                type: "nvarchar(15)",
                maxLength: 15,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Estado",
                table: "EventosHistorial",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SensorId",
                table: "EventosHistorial",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UsuarioResponsableId",
                table: "EventosHistorial",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Descripcion",
                table: "Comunidades",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Pais",
                table: "Comunidades",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "CerradaPorUsuarioId",
                table: "Alertas",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Estado",
                table: "Alertas",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReglaAlertaId",
                table: "Alertas",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReglaNombre",
                table: "Alertas",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "Umbral",
                table: "Alertas",
                type: "decimal(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReglasAlerta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    TipoSensor = table.Column<int>(type: "int", nullable: false),
                    ValorMinimo = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true),
                    ValorMaximo = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true),
                    Nivel = table.Column<int>(type: "int", nullable: false),
                    Fenomeno = table.Column<int>(type: "int", nullable: false),
                    Mensaje = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Activa = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaModificacion = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReglasAlerta", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventosHistorial_SensorId",
                table: "EventosHistorial",
                column: "SensorId");

            migrationBuilder.CreateIndex(
                name: "IX_EventosHistorial_UsuarioResponsableId",
                table: "EventosHistorial",
                column: "UsuarioResponsableId");

            migrationBuilder.CreateIndex(
                name: "IX_Alertas_CerradaPorUsuarioId",
                table: "Alertas",
                column: "CerradaPorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Alertas_ReglaAlertaId",
                table: "Alertas",
                column: "ReglaAlertaId");

            migrationBuilder.CreateIndex(
                name: "IX_ReglasAlerta_Nombre",
                table: "ReglasAlerta",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReglasAlerta_TipoSensor_Activa",
                table: "ReglasAlerta",
                columns: new[] { "TipoSensor", "Activa" });

            migrationBuilder.AddForeignKey(
                name: "FK_Alertas_ReglasAlerta_ReglaAlertaId",
                table: "Alertas",
                column: "ReglaAlertaId",
                principalTable: "ReglasAlerta",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Alertas_Usuarios_CerradaPorUsuarioId",
                table: "Alertas",
                column: "CerradaPorUsuarioId",
                principalTable: "Usuarios",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EventosHistorial_Sensores_SensorId",
                table: "EventosHistorial",
                column: "SensorId",
                principalTable: "Sensores",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EventosHistorial_Usuarios_UsuarioResponsableId",
                table: "EventosHistorial",
                column: "UsuarioResponsableId",
                principalTable: "Usuarios",
                principalColumn: "Id");

            // --- Datos existentes: se completan las columnas nuevas con valores coherentes. ---

            // Las comunidades de la carga inicial están en Nicaragua.
            migrationBuilder.Sql("UPDATE Comunidades SET Pais = N'Nicaragua' WHERE Pais = N'';");

            // Las lecturas anteriores se tomaron con el sensor en servicio y en su unidad actual.
            migrationBuilder.Sql(
                "UPDATE l SET l.UnidadMedida = s.UnidadMedida, l.EstadoSensor = 1 " +
                "FROM Lecturas l INNER JOIN Sensores s ON s.Id = l.SensorId;");

            // Estado de las alertas a partir de los campos que ya existían.
            migrationBuilder.Sql("UPDATE Alertas SET Estado = 2 WHERE FechaCierre IS NOT NULL;");
            migrationBuilder.Sql("UPDATE Alertas SET Estado = 1 WHERE FechaCierre IS NULL AND Reconocida = 1;");
            migrationBuilder.Sql("UPDATE Alertas SET ReglaNombre = CONCAT(N'Regla integrada: ', " +
                "CASE Fenomeno WHEN 1 THEN N'Inundacion' WHEN 2 THEN N'Sequia' WHEN 3 THEN N'Tormenta' " +
                "WHEN 4 THEN N'Helada' WHEN 5 THEN N'IncendioForestal' ELSE N'Ninguno' END) WHERE ReglaNombre = N'';");

            // El historial hereda sensor, estado y responsable de su alerta.
            migrationBuilder.Sql(
                "UPDATE e SET e.SensorId = a.SensorId, e.Estado = a.Estado, " +
                "e.UsuarioResponsableId = COALESCE(a.CerradaPorUsuarioId, a.ReconocidaPorUsuarioId) " +
                "FROM EventosHistorial e INNER JOIN Alertas a ON a.Id = e.AlertaId;");
            migrationBuilder.Sql("UPDATE EventosHistorial SET Estado = 2 WHERE FechaFin IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Alertas_ReglasAlerta_ReglaAlertaId",
                table: "Alertas");

            migrationBuilder.DropForeignKey(
                name: "FK_Alertas_Usuarios_CerradaPorUsuarioId",
                table: "Alertas");

            migrationBuilder.DropForeignKey(
                name: "FK_EventosHistorial_Sensores_SensorId",
                table: "EventosHistorial");

            migrationBuilder.DropForeignKey(
                name: "FK_EventosHistorial_Usuarios_UsuarioResponsableId",
                table: "EventosHistorial");

            migrationBuilder.DropTable(
                name: "ReglasAlerta");

            migrationBuilder.DropIndex(
                name: "IX_EventosHistorial_SensorId",
                table: "EventosHistorial");

            migrationBuilder.DropIndex(
                name: "IX_EventosHistorial_UsuarioResponsableId",
                table: "EventosHistorial");

            migrationBuilder.DropIndex(
                name: "IX_Alertas_CerradaPorUsuarioId",
                table: "Alertas");

            migrationBuilder.DropIndex(
                name: "IX_Alertas_ReglaAlertaId",
                table: "Alertas");

            migrationBuilder.DropColumn(
                name: "Descripcion",
                table: "Sensores");

            migrationBuilder.DropColumn(
                name: "FechaInstalacion",
                table: "Sensores");

            migrationBuilder.DropColumn(
                name: "Ubicacion",
                table: "Sensores");

            migrationBuilder.DropColumn(
                name: "EstadoSensor",
                table: "Lecturas");

            migrationBuilder.DropColumn(
                name: "UnidadMedida",
                table: "Lecturas");

            migrationBuilder.DropColumn(
                name: "Estado",
                table: "EventosHistorial");

            migrationBuilder.DropColumn(
                name: "SensorId",
                table: "EventosHistorial");

            migrationBuilder.DropColumn(
                name: "UsuarioResponsableId",
                table: "EventosHistorial");

            migrationBuilder.DropColumn(
                name: "Descripcion",
                table: "Comunidades");

            migrationBuilder.DropColumn(
                name: "Pais",
                table: "Comunidades");

            migrationBuilder.DropColumn(
                name: "CerradaPorUsuarioId",
                table: "Alertas");

            migrationBuilder.DropColumn(
                name: "Estado",
                table: "Alertas");

            migrationBuilder.DropColumn(
                name: "ReglaAlertaId",
                table: "Alertas");

            migrationBuilder.DropColumn(
                name: "ReglaNombre",
                table: "Alertas");

            migrationBuilder.DropColumn(
                name: "Umbral",
                table: "Alertas");
        }
    }
}

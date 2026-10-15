using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SWMatrc.Sensores.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class EsquemaInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sensores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ComunidadId = table.Column<int>(type: "int", nullable: false),
                    Codigo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    UnidadMedida = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    Ubicacion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FechaInstalacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Latitud = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    Longitud = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    ValorMinimo = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: false),
                    ValorMaximo = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: false),
                    VariacionMaxima = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: false),
                    ValorActual = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: false),
                    UltimaLectura = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NivelActual = table.Column<int>(type: "int", nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaModificacion = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sensores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Lecturas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SensorId = table.Column<int>(type: "int", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: false),
                    UnidadMedida = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    EstadoSensor = table.Column<int>(type: "int", nullable: false),
                    Nivel = table.Column<int>(type: "int", nullable: true),
                    FechaHora = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lecturas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Lecturas_Sensores_SensorId",
                        column: x => x.SensorId,
                        principalTable: "Sensores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Lecturas_FechaHora",
                table: "Lecturas",
                column: "FechaHora",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Lecturas_SensorId_FechaHora",
                table: "Lecturas",
                columns: new[] { "SensorId", "FechaHora" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Sensores_Codigo",
                table: "Sensores",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sensores_ComunidadId_Tipo",
                table: "Sensores",
                columns: new[] { "ComunidadId", "Tipo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Lecturas");

            migrationBuilder.DropTable(
                name: "Sensores");
        }
    }
}

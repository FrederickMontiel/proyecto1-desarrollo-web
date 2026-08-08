using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SWMatrc.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class MigracionInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Comunidades",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Municipio = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Departamento = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Latitud = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    Longitud = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    Poblacion = table.Column<int>(type: "int", nullable: false),
                    Activa = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaModificacion = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comunidades", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NombreCompleto = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Rol = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    UltimoAcceso = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaModificacion = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                });

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
                    Latitud = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    Longitud = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    ValorMinimo = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: false),
                    ValorMaximo = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: false),
                    VariacionMaxima = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: false),
                    ValorActual = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: false),
                    UltimaLectura = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UmbralAmarilloAlto = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true),
                    UmbralNaranjaAlto = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true),
                    UmbralRojoAlto = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true),
                    UmbralAmarilloBajo = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true),
                    UmbralNaranjaBajo = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true),
                    UmbralRojoBajo = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaModificacion = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sensores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sensores_Comunidades_ComunidadId",
                        column: x => x.ComunidadId,
                        principalTable: "Comunidades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Bitacora",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioId = table.Column<int>(type: "int", nullable: true),
                    UsuarioEmail = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Accion = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Entidad = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    EntidadId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Detalle = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DireccionIp = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    FechaHora = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bitacora", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bitacora_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Alertas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ComunidadId = table.Column<int>(type: "int", nullable: false),
                    SensorId = table.Column<int>(type: "int", nullable: true),
                    Nivel = table.Column<int>(type: "int", nullable: false),
                    Fenomeno = table.Column<int>(type: "int", nullable: false),
                    Mensaje = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    ValorDisparo = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true),
                    FechaHora = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaCierre = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Reconocida = table.Column<bool>(type: "bit", nullable: false),
                    ReconocidaPorUsuarioId = table.Column<int>(type: "int", nullable: true),
                    FechaReconocimiento = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaModificacion = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alertas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Alertas_Comunidades_ComunidadId",
                        column: x => x.ComunidadId,
                        principalTable: "Comunidades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Alertas_Sensores_SensorId",
                        column: x => x.SensorId,
                        principalTable: "Sensores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Alertas_Usuarios_ReconocidaPorUsuarioId",
                        column: x => x.ReconocidaPorUsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Lecturas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SensorId = table.Column<int>(type: "int", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: false),
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

            migrationBuilder.CreateTable(
                name: "EventosHistorial",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ComunidadId = table.Column<int>(type: "int", nullable: false),
                    AlertaId = table.Column<int>(type: "int", nullable: true),
                    Fenomeno = table.Column<int>(type: "int", nullable: false),
                    NivelMaximo = table.Column<int>(type: "int", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    FechaInicio = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaFin = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OrigenSensor = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ValorRegistrado = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaModificacion = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosHistorial", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventosHistorial_Alertas_AlertaId",
                        column: x => x.AlertaId,
                        principalTable: "Alertas",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EventosHistorial_Comunidades_ComunidadId",
                        column: x => x.ComunidadId,
                        principalTable: "Comunidades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Alertas_Abiertas",
                table: "Alertas",
                columns: new[] { "ComunidadId", "FechaCierre" },
                filter: "[FechaCierre] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Alertas_FechaHora",
                table: "Alertas",
                column: "FechaHora",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Alertas_ReconocidaPorUsuarioId",
                table: "Alertas",
                column: "ReconocidaPorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Alertas_SensorId",
                table: "Alertas",
                column: "SensorId");

            migrationBuilder.CreateIndex(
                name: "IX_Bitacora_Accion",
                table: "Bitacora",
                column: "Accion");

            migrationBuilder.CreateIndex(
                name: "IX_Bitacora_FechaHora",
                table: "Bitacora",
                column: "FechaHora",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Bitacora_UsuarioId",
                table: "Bitacora",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Comunidades_Nombre",
                table: "Comunidades",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventosHistorial_AlertaId",
                table: "EventosHistorial",
                column: "AlertaId");

            migrationBuilder.CreateIndex(
                name: "IX_EventosHistorial_ComunidadId_FechaInicio",
                table: "EventosHistorial",
                columns: new[] { "ComunidadId", "FechaInicio" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_EventosHistorial_Fenomeno",
                table: "EventosHistorial",
                column: "Fenomeno");

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

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_Email",
                table: "Usuarios",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bitacora");

            migrationBuilder.DropTable(
                name: "EventosHistorial");

            migrationBuilder.DropTable(
                name: "Lecturas");

            migrationBuilder.DropTable(
                name: "Alertas");

            migrationBuilder.DropTable(
                name: "Sensores");

            migrationBuilder.DropTable(
                name: "Usuarios");

            migrationBuilder.DropTable(
                name: "Comunidades");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SWMatrc.Community.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class EsquemaInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "comunidades",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    municipio = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    departamento = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    pais = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    latitud = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    longitud = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    poblacion = table.Column<int>(type: "integer", nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_modificacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comunidades", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_comunidades_activa",
                table: "comunidades",
                column: "activa");

            migrationBuilder.CreateIndex(
                name: "ix_comunidades_departamento_municipio",
                table: "comunidades",
                columns: new[] { "departamento", "municipio" });

            migrationBuilder.CreateIndex(
                name: "ix_comunidades_nombre",
                table: "comunidades",
                column: "nombre",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "comunidades");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SWMatrc.Sensores.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class SensoresFisicos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RecibidoEn",
                table: "Sensores",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ValorRecibido",
                table: "Sensores",
                type: "decimal(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecibidoEn",
                table: "Sensores");

            migrationBuilder.DropColumn(
                name: "ValorRecibido",
                table: "Sensores");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SWMatrc.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class HisteresisCierreAlertas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CiclosSinRiesgo",
                table: "Alertas",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CiclosSinRiesgo",
                table: "Alertas");
        }
    }
}

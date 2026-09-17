using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolManager.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Anzeigeorte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Placement",
                table: "Messages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1); // Bisherige Meldungen standen alle in der App.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Placement",
                table: "Messages");
        }
    }
}

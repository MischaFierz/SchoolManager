using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolManager.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeitenInDerApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AppPage",
                table: "Messages",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppPage",
                table: "Messages");
        }
    }
}

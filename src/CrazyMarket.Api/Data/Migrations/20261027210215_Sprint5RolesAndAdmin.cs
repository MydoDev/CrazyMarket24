using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrazyMarket.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Sprint5RolesAndAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Suppliers",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Role",
                table: "Suppliers");
        }
    }
}

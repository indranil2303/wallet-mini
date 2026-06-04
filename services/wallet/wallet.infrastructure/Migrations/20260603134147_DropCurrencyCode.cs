using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wallet.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropCurrencyCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Currency",
                table: "users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "users",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);
        }
    }
}

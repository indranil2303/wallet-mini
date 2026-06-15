using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wallet.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModifiedFxRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ModifedFxRate",
                table: "transactions",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModifedFxRate",
                table: "transactions");
        }
    }
}

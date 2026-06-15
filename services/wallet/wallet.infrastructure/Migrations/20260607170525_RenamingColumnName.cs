using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wallet.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenamingColumnName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ModifedFxRate",
                table: "transactions",
                newName: "ModifiedFxRate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ModifiedFxRate",
                table: "transactions",
                newName: "ModifedFxRate");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wallet.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixPostgresConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "transactions");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "transactions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "transactions");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "transactions",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }
    }
}

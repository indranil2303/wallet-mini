using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace wallet.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixCurrencyCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Amount",
                table: "transactions");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "wallet_account",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Inactive",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "Active");

            migrationBuilder.AlterColumn<string>(
                name: "CurrencyCode",
                table: "wallet_account",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<decimal>(
                name: "Balance",
                table: "wallet_account",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "wallet_account",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "users",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DestinationAmount",
                table: "transactions",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "DestinationCurrency",
                table: "transactions",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FeeCurrency",
                table: "transactions",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "FxRate",
                table: "transactions",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SourceAmount",
                table: "transactions",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "SourceCurrency",
                table: "transactions",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionFee",
                table: "transactions",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "currencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_currencies", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "currencies",
                columns: new[] { "Id", "Code", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1, "INR", true, "Indian Rupee" },
                    { 2, "USD", true, "US Dollar" },
                    { 3, "EUR", true, "Euro" },
                    { 4, "GBP", true, "British Pound" },
                    { 5, "AUD", true, "Australian Dollar" },
                    { 6, "CAD", true, "Canadian Dollar" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_wallet_account_Id",
                table: "wallet_account",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_currencies_Code",
                table: "currencies",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "currencies");

            migrationBuilder.DropIndex(
                name: "IX_wallet_account_Id",
                table: "wallet_account");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "wallet_account");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "users");

            migrationBuilder.DropColumn(
                name: "DestinationAmount",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "DestinationCurrency",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "FeeCurrency",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "FxRate",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "SourceAmount",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "SourceCurrency",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "TransactionFee",
                table: "transactions");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "wallet_account",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Active",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "Inactive");

            migrationBuilder.AlterColumn<string>(
                name: "CurrencyCode",
                table: "wallet_account",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3);

            migrationBuilder.AlterColumn<decimal>(
                name: "Balance",
                table: "wallet_account",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "transactions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }
    }
}

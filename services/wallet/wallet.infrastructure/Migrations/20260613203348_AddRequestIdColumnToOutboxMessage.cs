using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wallet.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestIdColumnToOutboxMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "outbox_messages",
                type: "uuid",
                maxLength: 200,
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_RequestId_EventKey",
                table: "outbox_messages",
                columns: new[] { "RequestId", "EventKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_outbox_messages_RequestId_EventKey",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "outbox_messages");
        }
    }
}

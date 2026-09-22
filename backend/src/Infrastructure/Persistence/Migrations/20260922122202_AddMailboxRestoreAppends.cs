// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMailboxRestoreAppends : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RestoreGeneration",
                table: "mailbox_accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "RestoreStatePosition",
                table: "mailbox_accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "mailbox_restore_appends",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    FolderAlias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FolderGeneration = table.Column<int>(type: "integer", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppendedUidValidity = table.Column<long>(type: "bigint", nullable: true),
                    AppendedUid = table.Column<long>(type: "bigint", nullable: true),
                    SettledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mailbox_restore_appends", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mailbox_restore_appends_stored_emails_StoredEmailId",
                        column: x => x.StoredEmailId,
                        principalTable: "stored_emails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_restore_appends_email",
                table: "mailbox_restore_appends",
                column: "StoredEmailId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_restore_appends_unanswered",
                table: "mailbox_restore_appends",
                columns: new[] { "MailboxAccountId", "IssuedAt" },
                filter: "\"SettledAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mailbox_restore_appends");

            migrationBuilder.DropColumn(
                name: "RestoreGeneration",
                table: "mailbox_accounts");

            migrationBuilder.DropColumn(
                name: "RestoreStatePosition",
                table: "mailbox_accounts");
        }
    }
}

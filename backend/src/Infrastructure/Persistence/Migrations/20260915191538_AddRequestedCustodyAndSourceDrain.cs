// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestedCustodyAndSourceDrain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ContentVerifiedAt",
                table: "stored_emails",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedCustody",
                table: "mailbox_accounts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValueSql: "'MirrorSource'");

            migrationBuilder.CreateTable(
                name: "mailbox_source_removals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MailFolderId = table.Column<long>(type: "bigint", nullable: false),
                    UidValidity = table.Column<long>(type: "bigint", nullable: false),
                    Uid = table.Column<long>(type: "bigint", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mailbox_source_removals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mailbox_source_removals_mail_folders_MailFolderId",
                        column: x => x.MailFolderId,
                        principalTable: "mail_folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_awaiting_drain",
                table: "stored_emails",
                columns: new[] { "MailboxAccountId", "ReceivedAt", "Id" },
                filter: "\"UidValidity\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_source_removals_occurrence",
                table: "mailbox_source_removals",
                columns: new[] { "MailFolderId", "UidValidity", "Uid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_source_removals_queue",
                table: "mailbox_source_removals",
                columns: new[] { "MailboxAccountId", "RecordedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mailbox_source_removals");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_awaiting_drain",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "ContentVerifiedAt",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "RequestedCustody",
                table: "mailbox_accounts");
        }
    }
}

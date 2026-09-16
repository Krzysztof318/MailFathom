// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FlagOnlyAuthoredDeletes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AuthoredDeleteFlaggedAt",
                table: "stored_emails",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeleteFlagSettledAt",
                table: "mailbox_mutations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServerDisposition",
                table: "mailbox_mutations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // Every delete recorded before the setting existed expunged its source, so the rows say so rather than
            // leaving the answer to the mapping's reading of a missing value.
            migrationBuilder.Sql(
                """UPDATE mailbox_mutations SET "ServerDisposition" = 'Expunge' WHERE "Mutation" = 'delete';""");

            migrationBuilder.CreateTable(
                name: "mailbox_flagged_deletes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MailFolderId = table.Column<long>(type: "bigint", nullable: false),
                    UidValidity = table.Column<long>(type: "bigint", nullable: false),
                    Uid = table.Column<long>(type: "bigint", nullable: false),
                    StoredEmailId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mailbox_flagged_deletes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mailbox_flagged_deletes_mail_folders_MailFolderId",
                        column: x => x.MailFolderId,
                        principalTable: "mail_folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_mailbox_flagged_deletes_stored_emails_StoredEmailId",
                        column: x => x.StoredEmailId,
                        principalTable: "stored_emails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_mutations_flagged_delete",
                table: "mailbox_mutations",
                columns: new[] { "MailFolderId", "UidValidity", "RecordedAt" },
                filter: "\"ServerDisposition\" = 'FlagDeleted' AND \"Stage\" = 'Completed' AND \"DeleteFlagSettledAt\" IS NULL AND \"SourceRemovalObservedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_flagged_deletes_occurrence",
                table: "mailbox_flagged_deletes",
                columns: new[] { "MailFolderId", "UidValidity", "Uid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_flagged_deletes_queue",
                table: "mailbox_flagged_deletes",
                columns: new[] { "MailFolderId", "UidValidity", "LastObservedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_mailbox_flagged_deletes_StoredEmailId",
                table: "mailbox_flagged_deletes",
                column: "StoredEmailId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mailbox_flagged_deletes");

            migrationBuilder.DropIndex(
                name: "ix_mailbox_mutations_flagged_delete",
                table: "mailbox_mutations");

            migrationBuilder.DropColumn(
                name: "AuthoredDeleteFlaggedAt",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "DeleteFlagSettledAt",
                table: "mailbox_mutations");

            migrationBuilder.DropColumn(
                name: "ServerDisposition",
                table: "mailbox_mutations");
        }
    }
}

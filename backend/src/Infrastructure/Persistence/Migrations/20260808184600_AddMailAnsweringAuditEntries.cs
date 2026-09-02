// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMailAnsweringAuditEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mail_answering_audit_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ChatEndpointAlias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    InstructionsVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Degradation = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mail_answering_audit_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mail_answering_audited_emails",
                columns: table => new
                {
                    MailAnsweringAuditEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    WasCited = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mail_answering_audited_emails", x => new { x.MailAnsweringAuditEntryId, x.StoredEmailId });
                    table.ForeignKey(
                        name: "FK_mail_answering_audited_emails_mail_answering_audit_entries_~",
                        column: x => x.MailAnsweringAuditEntryId,
                        principalTable: "mail_answering_audit_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_mail_answering_audited_emails_stored_emails_StoredEmailId",
                        column: x => x.StoredEmailId,
                        principalTable: "stored_emails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mail_answering_audit_entries_account_completed",
                table: "mail_answering_audit_entries",
                columns: new[] { "MailboxAccountId", "CompletedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_answering_audit_entries_run_account",
                table: "mail_answering_audit_entries",
                columns: new[] { "RunId", "MailboxAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mail_answering_audited_emails_StoredEmailId",
                table: "mail_answering_audited_emails",
                column: "StoredEmailId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mail_answering_audited_emails");

            migrationBuilder.DropTable(
                name: "mail_answering_audit_entries");
        }
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailThreads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EmailThreadId",
                table: "stored_emails",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentStoredEmailId",
                table: "stored_emails",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "email_threads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AssembledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MergedIntoEmailThreadId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_threads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_threads_mailbox_accounts_MailboxAccountId",
                        column: x => x.MailboxAccountId,
                        principalTable: "mailbox_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "email_thread_identifiers",
                columns: table => new
                {
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IdentifierHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmailThreadId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_thread_identifiers", x => new { x.MailboxAccountId, x.IdentifierHash });
                    table.ForeignKey(
                        name: "FK_email_thread_identifiers_email_threads_EmailThreadId",
                        column: x => x.EmailThreadId,
                        principalTable: "email_threads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stored_emails_ParentStoredEmailId",
                table: "stored_emails",
                column: "ParentStoredEmailId");

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_thread",
                table: "stored_emails",
                columns: new[] { "EmailThreadId", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_email_thread_identifiers_thread",
                table: "email_thread_identifiers",
                column: "EmailThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_email_threads_MailboxAccountId",
                table: "email_threads",
                column: "MailboxAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_stored_emails_email_threads_EmailThreadId",
                table: "stored_emails",
                column: "EmailThreadId",
                principalTable: "email_threads",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_stored_emails_stored_emails_ParentStoredEmailId",
                table: "stored_emails",
                column: "ParentStoredEmailId",
                principalTable: "stored_emails",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_stored_emails_email_threads_EmailThreadId",
                table: "stored_emails");

            migrationBuilder.DropForeignKey(
                name: "FK_stored_emails_stored_emails_ParentStoredEmailId",
                table: "stored_emails");

            migrationBuilder.DropTable(
                name: "email_thread_identifiers");

            migrationBuilder.DropTable(
                name: "email_threads");

            migrationBuilder.DropIndex(
                name: "IX_stored_emails_ParentStoredEmailId",
                table: "stored_emails");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_thread",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "EmailThreadId",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "ParentStoredEmailId",
                table: "stored_emails");
        }
    }
}

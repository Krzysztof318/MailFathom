// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndexObjectBackedContentAndRequireItsPayloadEmpty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_recurring_send_drafts_backend_payload",
                table: "recurring_send_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_outgoing_email_contents_backend_payload",
                table: "outgoing_email_contents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_mail_draft_contents_backend_payload",
                table: "mail_draft_contents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_email_message_contents_backend_payload",
                table: "email_message_contents");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_send_drafts_object_backed",
                table: "recurring_send_drafts",
                column: "Backend",
                filter: "\"Backend\" = 'ObjectStorage'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recurring_send_drafts_backend_payload",
                table: "recurring_send_drafts",
                sql: "(\"Backend\" = 'Database' AND \"DraftMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL AND \"DraftMime\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_email_contents_object_backed",
                table: "outgoing_email_contents",
                column: "Backend",
                filter: "\"Backend\" = 'ObjectStorage'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outgoing_email_contents_backend_payload",
                table: "outgoing_email_contents",
                sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL AND \"RawMime\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_mail_draft_contents_object_backed",
                table: "mail_draft_contents",
                column: "Backend",
                filter: "\"Backend\" = 'ObjectStorage'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_mail_draft_contents_backend_payload",
                table: "mail_draft_contents",
                sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL AND \"RawMime\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_email_message_contents_object_backed",
                table: "email_message_contents",
                column: "Backend",
                filter: "\"Backend\" = 'ObjectStorage'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_email_message_contents_backend_payload",
                table: "email_message_contents",
                sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL AND \"RawMime\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_recurring_send_drafts_object_backed",
                table: "recurring_send_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recurring_send_drafts_backend_payload",
                table: "recurring_send_drafts");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_email_contents_object_backed",
                table: "outgoing_email_contents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_outgoing_email_contents_backend_payload",
                table: "outgoing_email_contents");

            migrationBuilder.DropIndex(
                name: "ix_mail_draft_contents_object_backed",
                table: "mail_draft_contents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_mail_draft_contents_backend_payload",
                table: "mail_draft_contents");

            migrationBuilder.DropIndex(
                name: "ix_email_message_contents_object_backed",
                table: "email_message_contents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_email_message_contents_backend_payload",
                table: "email_message_contents");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recurring_send_drafts_backend_payload",
                table: "recurring_send_drafts",
                sql: "(\"Backend\" = 'Database' AND \"DraftMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outgoing_email_contents_backend_payload",
                table: "outgoing_email_contents",
                sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_mail_draft_contents_backend_payload",
                table: "mail_draft_contents",
                sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_email_message_contents_backend_payload",
                table: "email_message_contents",
                sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL)");
        }
    }
}

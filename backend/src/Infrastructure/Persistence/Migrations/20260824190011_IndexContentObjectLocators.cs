// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndexContentObjectLocators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_recurring_send_drafts_object_backed",
                table: "recurring_send_drafts");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_email_contents_object_backed",
                table: "outgoing_email_contents");

            migrationBuilder.DropIndex(
                name: "ix_mail_draft_contents_object_backed",
                table: "mail_draft_contents");

            migrationBuilder.DropIndex(
                name: "ix_email_message_contents_object_backed",
                table: "email_message_contents");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_send_drafts_object_locator",
                table: "recurring_send_drafts",
                column: "ObjectLocator",
                unique: true,
                filter: "\"Backend\" = 'ObjectStorage'");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_email_contents_object_locator",
                table: "outgoing_email_contents",
                column: "ObjectLocator",
                unique: true,
                filter: "\"Backend\" = 'ObjectStorage'");

            migrationBuilder.CreateIndex(
                name: "ix_mail_draft_contents_object_locator",
                table: "mail_draft_contents",
                column: "ObjectLocator",
                unique: true,
                filter: "\"Backend\" = 'ObjectStorage'");

            migrationBuilder.CreateIndex(
                name: "ix_email_message_contents_object_locator",
                table: "email_message_contents",
                column: "ObjectLocator",
                unique: true,
                filter: "\"Backend\" = 'ObjectStorage'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_recurring_send_drafts_object_locator",
                table: "recurring_send_drafts");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_email_contents_object_locator",
                table: "outgoing_email_contents");

            migrationBuilder.DropIndex(
                name: "ix_mail_draft_contents_object_locator",
                table: "mail_draft_contents");

            migrationBuilder.DropIndex(
                name: "ix_email_message_contents_object_locator",
                table: "email_message_contents");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_send_drafts_object_backed",
                table: "recurring_send_drafts",
                column: "Backend",
                filter: "\"Backend\" = 'ObjectStorage'");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_email_contents_object_backed",
                table: "outgoing_email_contents",
                column: "Backend",
                filter: "\"Backend\" = 'ObjectStorage'");

            migrationBuilder.CreateIndex(
                name: "ix_mail_draft_contents_object_backed",
                table: "mail_draft_contents",
                column: "Backend",
                filter: "\"Backend\" = 'ObjectStorage'");

            migrationBuilder.CreateIndex(
                name: "ix_email_message_contents_object_backed",
                table: "email_message_contents",
                column: "Backend",
                filter: "\"Backend\" = 'ObjectStorage'");
        }
    }
}

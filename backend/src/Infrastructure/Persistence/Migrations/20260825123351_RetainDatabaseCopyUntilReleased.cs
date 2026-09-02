// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetainDatabaseCopyUntilReleased : Migration
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

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ObjectVerifiedAt",
                table: "recurring_send_drafts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ObjectVerifiedAt",
                table: "outgoing_email_contents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ObjectVerifiedAt",
                table: "mail_draft_contents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ObjectVerifiedAt",
                table: "email_message_contents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_recurring_send_drafts_backend_payload",
                table: "recurring_send_drafts",
                sql: "(\"Backend\" = 'Database' AND \"DraftMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL AND \"ObjectVerifiedAt\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL\n    AND (\"DraftMime\" IS NULL OR \"ObjectVerifiedAt\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outgoing_email_contents_backend_payload",
                table: "outgoing_email_contents",
                sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL AND \"ObjectVerifiedAt\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL\n    AND (\"RawMime\" IS NULL OR \"ObjectVerifiedAt\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_mail_draft_contents_backend_payload",
                table: "mail_draft_contents",
                sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL AND \"ObjectVerifiedAt\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL\n    AND (\"RawMime\" IS NULL OR \"ObjectVerifiedAt\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_email_message_contents_backend_payload",
                table: "email_message_contents",
                sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL AND \"ObjectVerifiedAt\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL\n    AND (\"RawMime\" IS NULL OR \"ObjectVerifiedAt\" IS NOT NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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

            migrationBuilder.DropColumn(
                name: "ObjectVerifiedAt",
                table: "recurring_send_drafts");

            migrationBuilder.DropColumn(
                name: "ObjectVerifiedAt",
                table: "outgoing_email_contents");

            migrationBuilder.DropColumn(
                name: "ObjectVerifiedAt",
                table: "mail_draft_contents");

            migrationBuilder.DropColumn(
                name: "ObjectVerifiedAt",
                table: "email_message_contents");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recurring_send_drafts_backend_payload",
                table: "recurring_send_drafts",
                sql: "(\"Backend\" = 'Database' AND \"DraftMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL AND \"DraftMime\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outgoing_email_contents_backend_payload",
                table: "outgoing_email_contents",
                sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL AND \"RawMime\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_mail_draft_contents_backend_payload",
                table: "mail_draft_contents",
                sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL AND \"RawMime\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_email_message_contents_backend_payload",
                table: "email_message_contents",
                sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL AND \"RawMime\" IS NULL)");
        }
    }
}

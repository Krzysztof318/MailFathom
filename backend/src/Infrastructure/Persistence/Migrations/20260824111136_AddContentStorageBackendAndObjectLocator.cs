// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContentStorageBackendAndObjectLocator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte[]>(
                name: "DraftMime",
                table: "recurring_send_drafts",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "bytea");

            migrationBuilder.AddColumn<string>(
                name: "Backend",
                table: "recurring_send_drafts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Database");

            migrationBuilder.AddColumn<string>(
                name: "ObjectLocator",
                table: "recurring_send_drafts",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RawMime",
                table: "outgoing_email_contents",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "bytea");

            migrationBuilder.AddColumn<string>(
                name: "Backend",
                table: "outgoing_email_contents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Database");

            migrationBuilder.AddColumn<string>(
                name: "ObjectLocator",
                table: "outgoing_email_contents",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RawMime",
                table: "mail_draft_contents",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "bytea");

            migrationBuilder.AddColumn<string>(
                name: "Backend",
                table: "mail_draft_contents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Database");

            migrationBuilder.AddColumn<string>(
                name: "ObjectLocator",
                table: "mail_draft_contents",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RawMime",
                table: "email_message_contents",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "bytea");

            migrationBuilder.AddColumn<string>(
                name: "Backend",
                table: "email_message_contents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Database");

            migrationBuilder.AddColumn<string>(
                name: "ObjectLocator",
                table: "email_message_contents",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

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
                name: "Backend",
                table: "recurring_send_drafts");

            migrationBuilder.DropColumn(
                name: "ObjectLocator",
                table: "recurring_send_drafts");

            migrationBuilder.DropColumn(
                name: "Backend",
                table: "outgoing_email_contents");

            migrationBuilder.DropColumn(
                name: "ObjectLocator",
                table: "outgoing_email_contents");

            migrationBuilder.DropColumn(
                name: "Backend",
                table: "mail_draft_contents");

            migrationBuilder.DropColumn(
                name: "ObjectLocator",
                table: "mail_draft_contents");

            migrationBuilder.DropColumn(
                name: "Backend",
                table: "email_message_contents");

            migrationBuilder.DropColumn(
                name: "ObjectLocator",
                table: "email_message_contents");

            migrationBuilder.AlterColumn<byte[]>(
                name: "DraftMime",
                table: "recurring_send_drafts",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RawMime",
                table: "outgoing_email_contents",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RawMime",
                table: "mail_draft_contents",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RawMime",
                table: "email_message_contents",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);
        }
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalMailFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LocalMailFolderId",
                table: "stored_emails",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustodyPhase",
                table: "mailbox_accounts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValueSql: "'Mirrored'");

            migrationBuilder.AddColumn<int>(
                name: "LocalMailFoldersRevision",
                table: "mailbox_accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "local_mail_folders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    NameKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceFolderAlias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ErasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_mail_folders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_local_mail_folders_local_mail_folders_ParentId",
                        column: x => x.ParentId,
                        principalTable: "local_mail_folders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_local_mail_folders_mailbox_accounts_UserId_MailboxAccountId",
                        columns: x => new { x.UserId, x.MailboxAccountId },
                        principalTable: "mailbox_accounts",
                        principalColumns: new[] { "UserId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stored_emails_LocalMailFolderId",
                table: "stored_emails",
                column: "LocalMailFolderId",
                filter: "\"LocalMailFolderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_local_mail_folders_ParentId",
                table: "local_mail_folders",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "ix_local_mail_folders_user_account_parent_name",
                table: "local_mail_folders",
                columns: new[] { "UserId", "MailboxAccountId", "ParentId", "NameKey" },
                unique: true,
                filter: "\"ErasedAt\" IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_local_mail_folders_user_account_role",
                table: "local_mail_folders",
                columns: new[] { "UserId", "MailboxAccountId", "Role" },
                unique: true,
                filter: "\"Role\" IS NOT NULL AND \"ErasedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_local_mail_folders_UserId_MailboxAccountId_ErasedAt",
                table: "local_mail_folders",
                columns: new[] { "UserId", "MailboxAccountId", "ErasedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_stored_emails_local_mail_folders_LocalMailFolderId",
                table: "stored_emails",
                column: "LocalMailFolderId",
                principalTable: "local_mail_folders",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_stored_emails_local_mail_folders_LocalMailFolderId",
                table: "stored_emails");

            migrationBuilder.DropTable(
                name: "local_mail_folders");

            migrationBuilder.DropIndex(
                name: "IX_stored_emails_LocalMailFolderId",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "LocalMailFolderId",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "CustodyPhase",
                table: "mailbox_accounts");

            migrationBuilder.DropColumn(
                name: "LocalMailFoldersRevision",
                table: "mailbox_accounts");
        }
    }
}

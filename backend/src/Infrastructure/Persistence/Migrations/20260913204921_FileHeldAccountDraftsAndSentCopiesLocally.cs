// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FileHeldAccountDraftsAndSentCopiesLocally : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FiledRevision",
                table: "mail_drafts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FiledStoredEmailId",
                table: "mail_drafts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_filed_sent_copy",
                table: "stored_emails",
                columns: new[] { "UserId", "MailboxAccountId", "InternetMessageId" },
                filter: "\"FiledFromOutgoingEmailId\" IS NOT NULL AND \"UidValidity\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stored_emails_filed_sent_copy",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "FiledRevision",
                table: "mail_drafts");

            migrationBuilder.DropColumn(
                name: "FiledStoredEmailId",
                table: "mail_drafts");
        }
    }
}

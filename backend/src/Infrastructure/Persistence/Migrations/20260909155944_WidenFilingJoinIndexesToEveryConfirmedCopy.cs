// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WidenFilingJoinIndexesToEveryConfirmedCopy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outgoing_email_filings_message_id",
                table: "outgoing_email_filings");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_email_filings_placement",
                table: "outgoing_email_filings");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_email_filings_message_id",
                table: "outgoing_email_filings",
                columns: new[] { "UserId", "MailboxAccountId", "InternetMessageId" },
                filter: "\"Stage\" = 'Confirmed'");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_email_filings_placement",
                table: "outgoing_email_filings",
                columns: new[] { "UserId", "MailboxAccountId", "FolderPath", "PlacementUidValidity", "PlacementUid" },
                filter: "\"Stage\" = 'Confirmed'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outgoing_email_filings_message_id",
                table: "outgoing_email_filings");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_email_filings_placement",
                table: "outgoing_email_filings");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_email_filings_message_id",
                table: "outgoing_email_filings",
                columns: new[] { "UserId", "MailboxAccountId", "InternetMessageId" },
                filter: "\"ObservedAt\" IS NULL AND \"Stage\" = 'Confirmed'");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_email_filings_placement",
                table: "outgoing_email_filings",
                columns: new[] { "UserId", "MailboxAccountId", "FolderPath", "PlacementUidValidity", "PlacementUid" },
                filter: "\"ObservedAt\" IS NULL AND \"Stage\" = 'Confirmed'");
        }
    }
}

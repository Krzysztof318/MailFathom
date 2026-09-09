// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndexStandingFilingsByFilingAndAppend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_outgoing_email_filings_recent_by_filing",
                table: "outgoing_email_filings",
                columns: new[] { "UserId", "MailboxAccountId", "Filing", "AppendedAt", "OutgoingEmailId" },
                filter: "\"Stage\" = 'Confirmed'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outgoing_email_filings_recent_by_filing",
                table: "outgoing_email_filings");
        }
    }
}

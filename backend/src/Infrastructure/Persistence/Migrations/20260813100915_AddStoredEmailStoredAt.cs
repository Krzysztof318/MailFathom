// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStoredEmailStoredAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Npgsql writes the minimum value as `-infinity`, which is the backfill this column wants rather than an
            // artefact of it: a row stored before this migration has by definition waited longer than any classification
            // wait a deployment can configure, so it is eligible immediately. Stamping the upgrade instant instead would
            // hold a whole mailbox out of the index for one more wait apiece.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StoredAt",
                table: "stored_emails",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StoredAt",
                table: "stored_emails");
        }
    }
}

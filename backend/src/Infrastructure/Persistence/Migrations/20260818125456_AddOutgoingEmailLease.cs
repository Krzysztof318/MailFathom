// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutgoingEmailLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Npgsql writes the minimum value as `-infinity`, so every row that existed before this column did is due
            // at once rather than at the instant of the upgrade: a send already waiting has waited longer than any
            // backoff this deployment configures.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AvailableAt",
                table: "outgoing_emails",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                table: "outgoing_emails",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseOwner",
                table: "outgoing_emails",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_emails_claimable",
                table: "outgoing_emails",
                columns: new[] { "MailboxAccountId", "AvailableAt", "Id" },
                filter: "\"Stage\" = 'Recorded'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outgoing_emails_claimable",
                table: "outgoing_emails");

            migrationBuilder.DropColumn(
                name: "AvailableAt",
                table: "outgoing_emails");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "outgoing_emails");

            migrationBuilder.DropColumn(
                name: "LeaseOwner",
                table: "outgoing_emails");
        }
    }
}

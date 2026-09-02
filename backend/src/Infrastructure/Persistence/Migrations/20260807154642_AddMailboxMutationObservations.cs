// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMailboxMutationObservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PlacementObservedAt",
                table: "mailbox_mutations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SourceRemovalObservedAt",
                table: "mailbox_mutations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_mutations_placement",
                table: "mailbox_mutations",
                columns: new[] { "MailboxAccountId", "DestinationFolderPath", "PlacementUidValidity", "PlacementUid" },
                filter: "\"PlacementObservedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_mailbox_mutations_placement",
                table: "mailbox_mutations");

            migrationBuilder.DropColumn(
                name: "PlacementObservedAt",
                table: "mailbox_mutations");

            migrationBuilder.DropColumn(
                name: "SourceRemovalObservedAt",
                table: "mailbox_mutations");
        }
    }
}

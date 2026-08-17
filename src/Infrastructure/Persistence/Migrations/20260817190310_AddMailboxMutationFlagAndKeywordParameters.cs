// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMailboxMutationFlagAndKeywordParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DesiredFlaggedState",
                table: "mailbox_mutations",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "Keywords",
                table: "mailbox_mutations",
                type: "text[]",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DesiredFlaggedState",
                table: "mailbox_mutations");

            migrationBuilder.DropColumn(
                name: "Keywords",
                table: "mailbox_mutations");
        }
    }
}

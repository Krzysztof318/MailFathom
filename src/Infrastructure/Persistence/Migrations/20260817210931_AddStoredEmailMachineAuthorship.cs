// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStoredEmailMachineAuthorship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MachineAuthorshipBand",
                table: "stored_emails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "NotAssessed");

            migrationBuilder.AddColumn<double>(
                name: "MachineAuthorshipLikelihood",
                table: "stored_emails",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "MachineAuthorshipProfileRevision",
                table: "stored_emails",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MachineAuthorshipSignals",
                table: "stored_emails",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MachineAuthorshipBand",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "MachineAuthorshipLikelihood",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "MachineAuthorshipProfileRevision",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "MachineAuthorshipSignals",
                table: "stored_emails");
        }
    }
}

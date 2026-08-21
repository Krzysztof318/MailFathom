// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobFailureRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastFailureClassification",
                table: "jobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastFailureReason",
                table: "jobs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            // The terminal failure state is now named for what it is, and the column holds a state's name rather than
            // an ordinal, so a row written before this migration still reads 'Failed'. Nothing else would correct it,
            // and such a row would sit outside every query about dead letters — which is the one job state an operator
            // is meant to act on.
            migrationBuilder.Sql("""UPDATE jobs SET "State" = 'DeadLettered' WHERE "State" = 'Failed';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE jobs SET "State" = 'Failed' WHERE "State" = 'DeadLettered';""");

            migrationBuilder.DropColumn(
                name: "LastFailureClassification",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "LastFailureReason",
                table: "jobs");
        }
    }
}

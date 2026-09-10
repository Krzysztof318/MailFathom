// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpentClientAssertions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "spent_client_assertions",
                columns: table => new
                {
                    CredentialKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Identifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spent_client_assertions", x => new { x.CredentialKey, x.Identifier });
                });

            migrationBuilder.CreateIndex(
                name: "ix_spent_client_assertions_expires_at",
                table: "spent_client_assertions",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "spent_client_assertions");
        }
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdmitUsersOnTheAdministrativeEndpoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "AllowedSourceNetworks",
                table: "user_credentials",
                type: "text[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::text[]");

            migrationBuilder.AddColumn<string[]>(
                name: "Surfaces",
                table: "user_credentials",
                type: "text[]",
                nullable: false,
                defaultValueSql: "ARRAY['mcp','client']::text[]");

            migrationBuilder.CreateTable(
                name: "default_administrator",
                columns: table => new
                {
                    Id = table.Column<short>(type: "smallint", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PasswordSettingAppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_default_administrator", x => x.Id);
                    table.CheckConstraint("ck_default_administrator_single_row", "\"Id\" = 1");
                    table.ForeignKey(
                        name: "FK_default_administrator_settings_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "settings_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_default_administrator_UserId",
                table: "default_administrator",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "default_administrator");

            migrationBuilder.DropColumn(
                name: "AllowedSourceNetworks",
                table: "user_credentials");

            migrationBuilder.DropColumn(
                name: "Surfaces",
                table: "user_credentials");
        }
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserEndpointSwitches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ClientEndpointEnabled",
                table: "settings_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "McpEndpointEnabled",
                table: "settings_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClientEndpointEnabled",
                table: "settings_accounts");

            migrationBuilder.DropColumn(
                name: "McpEndpointEnabled",
                table: "settings_accounts");
        }
    }
}

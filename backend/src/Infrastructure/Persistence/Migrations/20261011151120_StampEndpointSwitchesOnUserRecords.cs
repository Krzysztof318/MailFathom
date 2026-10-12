// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StampEndpointSwitchesOnUserRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EndpointSwitchesOrganizationId",
                table: "settings_accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "EndpointSwitchesVersion",
                table: "settings_accounts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Written by hand, because a default cannot name another column. Every row is stamped as current: the
            // switches a row holds when this runs are what its record resolves to, since no settings policy is stored
            // by a build older than the one that carries this migration. What the default is left to say is the row
            // such a build inserts afterwards, whose switches nothing has derived.
            migrationBuilder.Sql(
                """
                UPDATE settings_accounts
                SET "EndpointSwitchesVersion" = "Version",
                    "EndpointSwitchesOrganizationId" = "OrganizationId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndpointSwitchesOrganizationId",
                table: "settings_accounts");

            migrationBuilder.DropColumn(
                name: "EndpointSwitchesVersion",
                table: "settings_accounts");
        }
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GroupMailAccountsIntoOrganizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "settings_mail_accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_settings_mail_accounts_organization",
                table: "settings_mail_accounts",
                column: "OrganizationId");

            migrationBuilder.AddForeignKey(
                name: "FK_settings_mail_accounts_organizations_OrganizationId",
                table: "settings_mail_accounts",
                column: "OrganizationId",
                principalTable: "organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_settings_mail_accounts_organizations_OrganizationId",
                table: "settings_mail_accounts");

            migrationBuilder.DropIndex(
                name: "ix_settings_mail_accounts_organization",
                table: "settings_mail_accounts");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "settings_mail_accounts");
        }
    }
}

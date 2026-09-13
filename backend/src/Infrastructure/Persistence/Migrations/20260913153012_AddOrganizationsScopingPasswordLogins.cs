// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationsScopingPasswordLogins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_user_credentials_method_lookup",
                table: "user_credentials");

            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "user_credentials",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "settings_accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "organizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ShortName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organizations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_credentials_method_organization_lookup",
                table: "user_credentials",
                columns: new[] { "Method", "OrganizationId", "Lookup" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_user_credentials_OrganizationId",
                table: "user_credentials",
                column: "OrganizationId");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_credentials_organization_scopes_password",
                table: "user_credentials",
                sql: "\"OrganizationId\" IS NULL OR \"Method\" = 'password'");

            migrationBuilder.CreateIndex(
                name: "ix_settings_accounts_organization",
                table: "settings_accounts",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "ix_organizations_short_name",
                table: "organizations",
                column: "ShortName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_settings_accounts_organizations_OrganizationId",
                table: "settings_accounts",
                column: "OrganizationId",
                principalTable: "organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_user_credentials_organizations_OrganizationId",
                table: "user_credentials",
                column: "OrganizationId",
                principalTable: "organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_settings_accounts_organizations_OrganizationId",
                table: "settings_accounts");

            migrationBuilder.DropForeignKey(
                name: "FK_user_credentials_organizations_OrganizationId",
                table: "user_credentials");

            migrationBuilder.DropTable(
                name: "organizations");

            migrationBuilder.DropIndex(
                name: "ix_user_credentials_method_organization_lookup",
                table: "user_credentials");

            migrationBuilder.DropIndex(
                name: "IX_user_credentials_OrganizationId",
                table: "user_credentials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_credentials_organization_scopes_password",
                table: "user_credentials");

            migrationBuilder.DropIndex(
                name: "ix_settings_accounts_organization",
                table: "settings_accounts");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "user_credentials");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "settings_accounts");

            migrationBuilder.CreateIndex(
                name: "ix_user_credentials_method_lookup",
                table: "user_credentials",
                columns: new[] { "Method", "Lookup" },
                unique: true);
        }
    }
}

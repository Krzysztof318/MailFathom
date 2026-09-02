// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResolveOwnerFacingCredentialsToOwnerRows : Migration
    {
        // Scaffolded as a drop and a create, which would have discarded every credential an owner already signs in
        // with, and written out as a rename instead. The table is the one the previous migration created for passwords
        // alone; what widens it is a method column and a grant, so the rows that exist keep working and keep the
        // identifiers an operator wrote down.
        //
        // The method backfill states what the existing rows already were: every row this migration can meet was written
        // by the password credential store, which was the only writer.
        //
        // The grant backfill cannot do the same, and grants nothing rather than guessing. What a password credential
        // was admitted with lived on the endpoint's Authentication entry, which narrowed to the operator's own
        // Permissions list wherever they wrote one — a list this statement cannot see. Backfilling the whole mail
        // surface would therefore widen exactly the deployments that had governed their credentials, handing a
        // read-only sign-in the ability to send mail on an upgrade nobody asked for. So an upgraded credential
        // authenticates and may do nothing until it is provisioned again with the grant its owner is meant to have,
        // which is the operator's action this release's changelog names.
        //
        // Neither default survives the statement that adds it, because the model declares none and a column carrying
        // one the model does not know about is a divergence nothing would report.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_owner_password_credentials_settings_accounts_OwnerId",
                table: "owner_password_credentials");

            migrationBuilder.DropPrimaryKey(
                name: "PK_owner_password_credentials",
                table: "owner_password_credentials");

            migrationBuilder.DropIndex(
                name: "ix_owner_password_credentials_username",
                table: "owner_password_credentials");

            migrationBuilder.DropIndex(
                name: "ix_owner_password_credentials_owner_created_at",
                table: "owner_password_credentials");

            migrationBuilder.RenameTable(
                name: "owner_password_credentials",
                newName: "owner_credentials");

            migrationBuilder.RenameColumn(
                name: "Username",
                table: "owner_credentials",
                newName: "Lookup");

            migrationBuilder.RenameColumn(
                name: "PasswordHash",
                table: "owner_credentials",
                newName: "Material");

            migrationBuilder.RenameColumn(
                name: "PasswordChangedAt",
                table: "owner_credentials",
                newName: "MaterialChangedAt");

            migrationBuilder.AlterColumn<string>(
                name: "Lookup",
                table: "owner_credentials",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "Material",
                table: "owner_credentials",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);

            migrationBuilder.AddColumn<string>(
                name: "Method",
                table: "owner_credentials",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValueSql: "'password'");

            migrationBuilder.AddColumn<string[]>(
                name: "Permissions",
                table: "owner_credentials",
                type: "text[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::text[]");

            migrationBuilder.Sql(
                """
                ALTER TABLE owner_credentials ALTER COLUMN "Method" DROP DEFAULT;
                ALTER TABLE owner_credentials ALTER COLUMN "Permissions" DROP DEFAULT;
                """);

            migrationBuilder.AddPrimaryKey(
                name: "PK_owner_credentials",
                table: "owner_credentials",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "ix_owner_credentials_method_lookup",
                table: "owner_credentials",
                columns: new[] { "Method", "Lookup" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_owner_credentials_owner_created_at",
                table: "owner_credentials",
                columns: new[] { "OwnerId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_owner_credentials_settings_accounts_OwnerId",
                table: "owner_credentials",
                column: "OwnerId",
                principalTable: "settings_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A credential provisioned for one of the three methods the previous schema had no column for cannot be
            // expressed by it, so reversing this drops those rows rather than narrowing them into a shape that would
            // authenticate the wrong way. The password credentials the table started with are kept.
            migrationBuilder.Sql("DELETE FROM owner_credentials WHERE \"Method\" <> 'password';");

            migrationBuilder.DropForeignKey(
                name: "FK_owner_credentials_settings_accounts_OwnerId",
                table: "owner_credentials");

            migrationBuilder.DropPrimaryKey(
                name: "PK_owner_credentials",
                table: "owner_credentials");

            migrationBuilder.DropIndex(
                name: "ix_owner_credentials_method_lookup",
                table: "owner_credentials");

            migrationBuilder.DropIndex(
                name: "ix_owner_credentials_owner_created_at",
                table: "owner_credentials");

            migrationBuilder.DropColumn(
                name: "Method",
                table: "owner_credentials");

            migrationBuilder.DropColumn(
                name: "Permissions",
                table: "owner_credentials");

            migrationBuilder.RenameTable(
                name: "owner_credentials",
                newName: "owner_password_credentials");

            migrationBuilder.RenameColumn(
                name: "Lookup",
                table: "owner_password_credentials",
                newName: "Username");

            migrationBuilder.RenameColumn(
                name: "Material",
                table: "owner_password_credentials",
                newName: "PasswordHash");

            migrationBuilder.RenameColumn(
                name: "MaterialChangedAt",
                table: "owner_password_credentials",
                newName: "PasswordChangedAt");

            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "owner_password_credentials",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "owner_password_credentials",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4096)",
                oldMaxLength: 4096,
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_owner_password_credentials",
                table: "owner_password_credentials",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "ix_owner_password_credentials_username",
                table: "owner_password_credentials",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_owner_password_credentials_owner_created_at",
                table: "owner_password_credentials",
                columns: new[] { "OwnerId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_owner_password_credentials_settings_accounts_OwnerId",
                table: "owner_password_credentials",
                column: "OwnerId",
                principalTable: "settings_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

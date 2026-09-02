// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// The generated shape would have left the label an empty string on every row that already existed, which is not a
    /// label and would have been the value an administrator read the deployment's one owner under. The statement
    /// between the generated operations is what makes it apply forward instead: the owner this deployment is already
    /// serving is labelled before the index that admits one row per label is created. One label is unique by
    /// construction here, because the only insert this table has ever taken is the single row
    /// <c>AddOwnerAccounts</c> provisions under <c>WHERE NOT EXISTS</c> — and were that ever untrue, the unique index
    /// would refuse this migration rather than admit two owners under one name.
    /// </remarks>
    public partial class AddOwnerLabelAndDocumentMarker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Added nullable and made required below rather than added with a default, for the reason the owner column
            // on mailbox_accounts was: a default left on the column would go on supplying a value for a row that named
            // no label, and the label is the one column here nothing may default.
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "settings_accounts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            // The label the owner an upgraded deployment already serves is read under, written while the column still
            // admits the absence and before the index that admits one row per label exists.
            migrationBuilder.Sql(
                """
                UPDATE settings_accounts SET "DisplayName" = 'owner' WHERE "DisplayName" IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "DisplayName",
                table: "settings_accounts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DocumentWrittenAtRuntime",
                table: "settings_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_settings_accounts_display_name",
                table: "settings_accounts",
                column: "DisplayName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_settings_accounts_display_name",
                table: "settings_accounts");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "settings_accounts");

            migrationBuilder.DropColumn(
                name: "DocumentWrittenAtRuntime",
                table: "settings_accounts");
        }
    }
}

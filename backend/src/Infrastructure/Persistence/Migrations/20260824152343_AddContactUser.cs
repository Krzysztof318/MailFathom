// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    // Authored against a base that did not yet carry 20260824190011_IndexContentObjectLocators, so this identifier
    // sorts before a migration that merged first and is never the newest one a filename sort finds. It is permanent,
    // because a database has already written it into __EFMigrationsHistory, and the ordering costs nothing at runtime:
    // the two touch different tables and either order leaves one schema. What it does cost is
    // scripts/script-migration.sh with no arguments, which reads that sort and would script the locator index instead.
    // Review this one's SQL by naming the pair:
    //   scripts/script-migration.sh 20260824142528_IndexObjectBackedContentAndRequireItsPayloadEmpty 20260824152343_AddContactUser

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The generated shape would have added both user columns with the zero identifier as their default and then
    /// pointed a foreign key at a row nothing had inserted, which fails on any database that already holds a contact.
    /// The statements between the generated operations are what make it apply forward instead: the columns arrive
    /// nullable, every contact already written is carried onto the user this deployment is already serving, every
    /// address row takes the user of the contact it hangs on, and only then do the columns become required and keyed.
    /// The user row itself is <c>AddUserAccounts</c>' work and always exists by the time this runs.
    /// </para>
    /// <para>
    /// The uniqueness over an address narrows from the whole table to one user's book, so nothing that was accepted
    /// before is refused now and no row has to be reconciled to apply this. Reverting it is the direction that cannot
    /// be taken freely: a book that has since acquired two users holding one address between them has no
    /// deployment-wide unique index to go back to, and <c>Down</c> fails on the index rather than discarding one of
    /// them.
    /// </para>
    /// </remarks>
    public partial class AddContactUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_contact_addresses_contacts_ContactId",
                table: "contact_addresses");

            migrationBuilder.DropIndex(
                name: "ix_contacts_display_name_sort_key_id",
                table: "contacts");

            migrationBuilder.DropIndex(
                name: "IX_contact_addresses_ContactId",
                table: "contact_addresses");

            migrationBuilder.DropIndex(
                name: "ix_contact_addresses_normalized_address",
                table: "contact_addresses");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "contacts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "contact_addresses",
                type: "uuid",
                nullable: true);

            // Every contact this deployment holds is one user's, because that is what a deployment served before this
            // release. The subquery names the row AddUserAccounts inserted rather than a value repeated here.
            migrationBuilder.Sql(
                """
                UPDATE contacts
                SET "UserId" = (SELECT "Id" FROM settings_accounts ORDER BY "CreatedAt", "Id" LIMIT 1)
                WHERE "UserId" IS NULL;
                """);

            // An address row's user is the user of the contact it hangs on, which is what the composite key below
            // then makes structural rather than a value this statement got right once.
            migrationBuilder.Sql(
                """
                UPDATE contact_addresses
                SET "UserId" = contacts."UserId"
                FROM contacts
                WHERE contacts."Id" = contact_addresses."ContactId"
                  AND contact_addresses."UserId" IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "contacts",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "contact_addresses",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_contacts_Id_UserId",
                table: "contacts",
                columns: new[] { "Id", "UserId" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_user_display_name_sort_key_id",
                table: "contacts",
                columns: new[] { "UserId", "DisplayNameSortKey", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_contact_addresses_ContactId_UserId",
                table: "contact_addresses",
                columns: new[] { "ContactId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "ix_contact_addresses_user_normalized_address",
                table: "contact_addresses",
                columns: new[] { "UserId", "NormalizedAddress" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_contact_addresses_contacts_ContactId_UserId",
                table: "contact_addresses",
                columns: new[] { "ContactId", "UserId" },
                principalTable: "contacts",
                principalColumns: new[] { "Id", "UserId" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_contacts_settings_accounts_UserId",
                table: "contacts",
                column: "UserId",
                principalTable: "settings_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_contact_addresses_contacts_ContactId_UserId",
                table: "contact_addresses");

            migrationBuilder.DropForeignKey(
                name: "FK_contacts_settings_accounts_UserId",
                table: "contacts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_contacts_Id_UserId",
                table: "contacts");

            migrationBuilder.DropIndex(
                name: "ix_contacts_user_display_name_sort_key_id",
                table: "contacts");

            migrationBuilder.DropIndex(
                name: "IX_contact_addresses_ContactId_UserId",
                table: "contact_addresses");

            migrationBuilder.DropIndex(
                name: "ix_contact_addresses_user_normalized_address",
                table: "contact_addresses");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "contact_addresses");

            migrationBuilder.CreateIndex(
                name: "ix_contacts_display_name_sort_key_id",
                table: "contacts",
                columns: new[] { "DisplayNameSortKey", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_contact_addresses_ContactId",
                table: "contact_addresses",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "ix_contact_addresses_normalized_address",
                table: "contact_addresses",
                column: "NormalizedAddress",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_contact_addresses_contacts_ContactId",
                table: "contact_addresses",
                column: "ContactId",
                principalTable: "contacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

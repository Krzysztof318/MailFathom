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
    //   scripts/script-migration.sh 20260824142528_IndexObjectBackedContentAndRequireItsPayloadEmpty 20260824152343_AddContactOwner

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The generated shape would have added both owner columns with the zero identifier as their default and then
    /// pointed a foreign key at a row nothing had inserted, which fails on any database that already holds a contact.
    /// The statements between the generated operations are what make it apply forward instead: the columns arrive
    /// nullable, every contact already written is carried onto the owner this deployment is already serving, every
    /// address row takes the owner of the contact it hangs on, and only then do the columns become required and keyed.
    /// The owner row itself is <c>AddOwnerAccounts</c>' work and always exists by the time this runs.
    /// </para>
    /// <para>
    /// The uniqueness over an address narrows from the whole table to one owner's book, so nothing that was accepted
    /// before is refused now and no row has to be reconciled to apply this. Reverting it is the direction that cannot
    /// be taken freely: a book that has since acquired two owners holding one address between them has no
    /// deployment-wide unique index to go back to, and <c>Down</c> fails on the index rather than discarding one of
    /// them.
    /// </para>
    /// </remarks>
    public partial class AddContactOwner : Migration
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
                name: "OwnerId",
                table: "contacts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "contact_addresses",
                type: "uuid",
                nullable: true);

            // Every contact this deployment holds is one owner's, because that is what a deployment served before this
            // release. The subquery names the row AddOwnerAccounts inserted rather than a value repeated here.
            migrationBuilder.Sql(
                """
                UPDATE contacts
                SET "OwnerId" = (SELECT "Id" FROM settings_accounts ORDER BY "CreatedAt", "Id" LIMIT 1)
                WHERE "OwnerId" IS NULL;
                """);

            // An address row's owner is the owner of the contact it hangs on, which is what the composite key below
            // then makes structural rather than a value this statement got right once.
            migrationBuilder.Sql(
                """
                UPDATE contact_addresses
                SET "OwnerId" = contacts."OwnerId"
                FROM contacts
                WHERE contacts."Id" = contact_addresses."ContactId"
                  AND contact_addresses."OwnerId" IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerId",
                table: "contacts",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerId",
                table: "contact_addresses",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_contacts_Id_OwnerId",
                table: "contacts",
                columns: new[] { "Id", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_owner_display_name_sort_key_id",
                table: "contacts",
                columns: new[] { "OwnerId", "DisplayNameSortKey", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_contact_addresses_ContactId_OwnerId",
                table: "contact_addresses",
                columns: new[] { "ContactId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "ix_contact_addresses_owner_normalized_address",
                table: "contact_addresses",
                columns: new[] { "OwnerId", "NormalizedAddress" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_contact_addresses_contacts_ContactId_OwnerId",
                table: "contact_addresses",
                columns: new[] { "ContactId", "OwnerId" },
                principalTable: "contacts",
                principalColumns: new[] { "Id", "OwnerId" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_contacts_settings_accounts_OwnerId",
                table: "contacts",
                column: "OwnerId",
                principalTable: "settings_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_contact_addresses_contacts_ContactId_OwnerId",
                table: "contact_addresses");

            migrationBuilder.DropForeignKey(
                name: "FK_contacts_settings_accounts_OwnerId",
                table: "contacts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_contacts_Id_OwnerId",
                table: "contacts");

            migrationBuilder.DropIndex(
                name: "ix_contacts_owner_display_name_sort_key_id",
                table: "contacts");

            migrationBuilder.DropIndex(
                name: "IX_contact_addresses_ContactId_OwnerId",
                table: "contact_addresses");

            migrationBuilder.DropIndex(
                name: "ix_contact_addresses_owner_normalized_address",
                table: "contact_addresses");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "OwnerId",
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

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SplitContactBooksByHolder : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// <para>
        /// A contact book stops being one user's and becomes either a user's own or a mail account's, so every row has
        /// to say which. A contact somebody wrote down carries over unchanged, filed under the user it was already
        /// filed under. A collected one has no account column to move into, because nothing in the old schema recorded
        /// which mailbox picked the address up — so it is carried over only where the deployment itself answers that:
        /// where the user it was filed under is assigned exactly one mailbox and that mailbox is assigned exactly that
        /// one user, the mailbox it arrived on is the only one it can have arrived on.
        /// </para>
        /// <para>
        /// Everything else collected is deleted rather than attributed to a mailbox this migration would have to
        /// guess — a wrong guess would publish one user's correspondents to everybody else assigned a shared mailbox,
        /// which is the disclosure this whole change exists to prevent. Collection rebuilds those rows from the mail
        /// that arrives next, which is the same thing it does after <c>mfctl contact delete-collected</c>. Nothing a
        /// user or an operator wrote down is touched either way.
        /// </para>
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // The old shape's key and indexes come off first, because the data work below moves a row between books —
            // which a foreign key naming the user, and a uniqueness scoped to one, would each refuse.
            migrationBuilder.DropForeignKey(
                name: "FK_contact_addresses_contacts_ContactId_UserId",
                table: "contact_addresses");

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

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "contacts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // Added nullable and tightened once they are filled, so no column default outlives the backfill: a default
            // the model never declared would sit in the schema forever and let a later insert that forgot the book key
            // succeed.
            migrationBuilder.AddColumn<string>(
                name: "BookHolderId",
                table: "contacts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MailboxAccountId",
                table: "contacts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BookHolderId",
                table: "contact_addresses",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            // The one case the deployment answers on its own: a user assigned exactly one mailbox that is assigned
            // exactly that one user. The mailbox row is joined rather than assumed, so a collected contact is only ever
            // moved onto an account the mail graph actually holds — which is also what the new foreign key requires.
            migrationBuilder.Sql(
                """
                WITH sole_assignment AS (
                    SELECT assignment."UserId" AS user_id, assignment."MailAccountId"::text AS account_id
                    FROM mail_account_assignments assignment
                    WHERE (
                            SELECT count(*) FROM mail_account_assignments held
                            WHERE held."UserId" = assignment."UserId") = 1
                      AND (
                            SELECT count(*) FROM mail_account_assignments shared
                            WHERE shared."MailAccountId" = assignment."MailAccountId") = 1)
                UPDATE contacts
                SET "MailboxAccountId" = sole_assignment.account_id, "UserId" = NULL
                FROM sole_assignment
                JOIN mailbox_accounts ON mailbox_accounts."Id" = sole_assignment.account_id
                WHERE contacts."Origin" = 'Collected' AND contacts."UserId" = sole_assignment.user_id;
                """);

            // The addresses go first and by hand, the foreign key that would have cascaded them having been dropped
            // above.
            migrationBuilder.Sql(
                """
                DELETE FROM contact_addresses
                WHERE "ContactId" IN (
                    SELECT "Id" FROM contacts WHERE "Origin" = 'Collected' AND "UserId" IS NOT NULL);
                """);

            migrationBuilder.Sql("""DELETE FROM contacts WHERE "Origin" = 'Collected' AND "UserId" IS NOT NULL;""");

            // The book key repeats whichever holder the row ended up with, and an address row takes its contact's
            // rather than its own former user column: a carried-over contact is in the account's book now, and its
            // addresses belong to the same book by construction.
            migrationBuilder.Sql(
                """
                UPDATE contacts
                SET "BookHolderId" = CASE
                    WHEN "UserId" IS NULL THEN 'account:' || "MailboxAccountId"
                    ELSE 'user:' || "UserId"::text END;
                """);

            migrationBuilder.Sql(
                """
                UPDATE contact_addresses
                SET "BookHolderId" = contacts."BookHolderId"
                FROM contacts
                WHERE contacts."Id" = contact_addresses."ContactId";
                """);

            migrationBuilder.AlterColumn<string>(
                name: "BookHolderId",
                table: "contacts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BookHolderId",
                table: "contact_addresses",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "contact_addresses");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_contacts_Id_BookHolderId",
                table: "contacts",
                columns: new[] { "Id", "BookHolderId" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_book_holder_display_name_sort_key_id",
                table: "contacts",
                columns: new[] { "BookHolderId", "DisplayNameSortKey", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_contacts_MailboxAccountId",
                table: "contacts",
                column: "MailboxAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_contacts_UserId",
                table: "contacts",
                column: "UserId");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contacts_book_holder",
                table: "contacts",
                sql: "((\"UserId\" IS NOT NULL)::int + (\"MailboxAccountId\" IS NOT NULL)::int) = 1\nAND \"BookHolderId\" = CASE WHEN \"UserId\" IS NULL THEN 'account:' || \"MailboxAccountId\" ELSE 'user:' || \"UserId\"::text END\nAND \"Origin\" = CASE WHEN \"UserId\" IS NULL THEN 'Collected' ELSE 'Asserted' END");

            migrationBuilder.CreateIndex(
                name: "ix_contact_addresses_book_holder_normalized_address",
                table: "contact_addresses",
                columns: new[] { "BookHolderId", "NormalizedAddress" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contact_addresses_ContactId_BookHolderId",
                table: "contact_addresses",
                columns: new[] { "ContactId", "BookHolderId" });

            migrationBuilder.AddForeignKey(
                name: "FK_contact_addresses_contacts_ContactId_BookHolderId",
                table: "contact_addresses",
                columns: new[] { "ContactId", "BookHolderId" },
                principalTable: "contacts",
                principalColumns: new[] { "Id", "BookHolderId" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_contacts_mailbox_accounts_MailboxAccountId",
                table: "contacts",
                column: "MailboxAccountId",
                principalTable: "mailbox_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        /// <remarks>
        /// The mirror of the loss above, in the other direction: a book that belongs to a mail account has no user
        /// column to move into, so those rows go rather than being attributed to whoever happens to be assigned that
        /// mailbox.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropForeignKey(
                name: "FK_contact_addresses_contacts_ContactId_BookHolderId",
                table: "contact_addresses");

            migrationBuilder.DropForeignKey(
                name: "FK_contacts_mailbox_accounts_MailboxAccountId",
                table: "contacts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_contacts_Id_BookHolderId",
                table: "contacts");

            migrationBuilder.DropIndex(
                name: "ix_contacts_book_holder_display_name_sort_key_id",
                table: "contacts");

            migrationBuilder.DropIndex(
                name: "IX_contacts_MailboxAccountId",
                table: "contacts");

            migrationBuilder.DropIndex(
                name: "IX_contacts_UserId",
                table: "contacts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_contacts_book_holder",
                table: "contacts");

            migrationBuilder.Sql("""DELETE FROM contacts WHERE "MailboxAccountId" IS NOT NULL;""");

            migrationBuilder.DropIndex(
                name: "ix_contact_addresses_book_holder_normalized_address",
                table: "contact_addresses");

            migrationBuilder.DropIndex(
                name: "IX_contact_addresses_ContactId_BookHolderId",
                table: "contact_addresses");

            migrationBuilder.DropColumn(
                name: "BookHolderId",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "MailboxAccountId",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "BookHolderId",
                table: "contact_addresses");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "contacts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "contact_addresses",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE contact_addresses
                SET "UserId" = contacts."UserId"
                FROM contacts
                WHERE contacts."Id" = contact_addresses."ContactId";
                """);

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
        }
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// SQL only and no model change, deliberately: both blocks live inside a <c>jsonb</c> document that the schema
    /// declares as one column, so moving them between two documents changes what those columns hold and nothing EF
    /// maps. What it does change is which settings the binder accepts on which record, which is why the rows have to
    /// move in the same release: a user record still carrying either block is refused as a property nothing binds, and
    /// an account carrying neither is classified and scanned as though its user had asked for nothing.
    /// </remarks>
    public partial class HoldSpamAndSensitiveContentOnTheMailAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Each user's two blocks are carried into every account assigned to them and then removed from the record.
            // The account's own value wins where it already carries one, which is what the concatenation order says.
            // An account is assigned to one user — the assignment index is unique on the account — so no account is
            // written from two records and the join can only match once.
            migrationBuilder.Sql(
                """
                WITH carried AS (
                    SELECT a."MailAccountId" AS account_id,
                           (CASE WHEN u."Document" ? 'SpamClassification'
                                 THEN jsonb_build_object('SpamClassification', u."Document" -> 'SpamClassification')
                                 ELSE '{}'::jsonb END)
                           || (CASE WHEN u."Document" ? 'SensitiveContent'
                                    THEN jsonb_build_object('SensitiveContent', u."Document" -> 'SensitiveContent')
                                    ELSE '{}'::jsonb END) AS blocks
                    FROM mail_account_assignments AS a
                    JOIN settings_accounts AS u ON u."Id" = a."UserId"
                    WHERE u."Document" ? 'SpamClassification' OR u."Document" ? 'SensitiveContent'
                )
                UPDATE settings_mail_accounts AS m
                SET "Document" = carried.blocks || m."Document",
                    "Version" = m."Version" + 1,
                    "UpdatedAt" = now()
                FROM carried
                WHERE m."Id" = carried.account_id;

                UPDATE settings_accounts
                SET "Document" = "Document" - 'SpamClassification' - 'SensitiveContent',
                    "Version" = "Version" + 1
                WHERE "Document" ? 'SpamClassification' OR "Document" ? 'SensitiveContent';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The blocks go back onto the user record, from the first account assigned to them that carries one: a
            // user record holds one answer for all their mail, so a second account stating something else has nowhere
            // to be written back to and its settings are dropped rather than composed into an answer nobody chose.
            migrationBuilder.Sql(
                """
                WITH carried AS (
                    SELECT a."UserId" AS user_id,
                           (CASE WHEN m."Document" ? 'SpamClassification'
                                 THEN jsonb_build_object('SpamClassification', m."Document" -> 'SpamClassification')
                                 ELSE '{}'::jsonb END)
                           || (CASE WHEN m."Document" ? 'SensitiveContent'
                                    THEN jsonb_build_object('SensitiveContent', m."Document" -> 'SensitiveContent')
                                    ELSE '{}'::jsonb END) AS blocks,
                           row_number() OVER (PARTITION BY a."UserId" ORDER BY a."AssignedAt", m."Id") AS carrier
                    FROM mail_account_assignments AS a
                    JOIN settings_mail_accounts AS m ON m."Id" = a."MailAccountId"
                    WHERE m."Document" ? 'SpamClassification' OR m."Document" ? 'SensitiveContent'
                )
                UPDATE settings_accounts AS u
                SET "Document" = u."Document" || carried.blocks,
                    "Version" = u."Version" + 1
                FROM carried
                WHERE u."Id" = carried.user_id AND carried.carrier = 1;

                UPDATE settings_mail_accounts
                SET "Document" = "Document" - 'SpamClassification' - 'SensitiveContent',
                    "Version" = "Version" + 1,
                    "UpdatedAt" = now()
                WHERE "Document" ? 'SpamClassification' OR "Document" ? 'SensitiveContent';
                """);
        }
    }
}

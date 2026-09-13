// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveMailAccountsIntoRecordsOfTheirOwn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "settings_mail_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    NormalizedEmailAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Document = table.Column<string>(type: "jsonb", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settings_mail_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mail_account_assignments",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MailAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mail_account_assignments", x => new { x.UserId, x.MailAccountId });
                    table.ForeignKey(
                        name: "fk_mail_account_assignments_settings_accounts",
                        column: x => x.UserId,
                        principalTable: "settings_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mail_account_assignments_settings_mail_accounts",
                        column: x => x.MailAccountId,
                        principalTable: "settings_mail_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mail_account_assignments_MailAccountId",
                table: "mail_account_assignments",
                column: "MailAccountId");

            migrationBuilder.CreateIndex(
                name: "ix_settings_mail_accounts_normalized_email_address",
                table: "settings_mail_accounts",
                column: "NormalizedEmailAddress",
                unique: true,
                filter: "\"NormalizedEmailAddress\" IS NOT NULL");

            // Stored mail is keyed by the operator-chosen identifier this migration retires, so it is discarded rather
            // than rewritten and every account synchronizes again under its record's identifier. The statements are the
            // per-account erasure's own set: the tables no cascade from mailbox_accounts reaches, then mailbox_accounts
            // itself. A refresh token is sealed with the old identifier as associated data, so it cannot be carried and
            // an OAuth mailbox is consented again. A schedule for a rule names the account inside its key.
            // ponytail: payloads already moved to object storage are not released here; a content move reclaims them.
            migrationBuilder.Sql(
                """
                DELETE FROM mail_answering_audit_entries;
                DELETE FROM mail_drafts;
                DELETE FROM mail_rederivation_positions;
                DELETE FROM mail_rederivation_runs;
                DELETE FROM mail_rule_evaluation_runs;
                DELETE FROM mailbox_mutation_audit_entries;
                DELETE FROM mailbox_refresh_tokens;
                DELETE FROM outgoing_emails;
                DELETE FROM recurring_sends;
                DELETE FROM spam_classification_runs;
                DELETE FROM jobs WHERE "MailboxAccountId" IS NOT NULL;
                DELETE FROM mailbox_accounts;
                DELETE FROM job_schedules WHERE "ScheduleId" LIKE 'mail-rules:%';
                """);

            // Each account a user's record declared becomes a record of its own assigned to that user. The address is
            // the login where the login is one and the delivery sender otherwise; one that is not a single address, or
            // that an earlier account already claimed, is left unset for an administrator to state, because the index
            // allows no second holder. Both collection shapes a record can carry — an array, and an object keyed by
            // position — are read in order.
            migrationBuilder.Sql(
                """
                WITH declared AS (
                    SELECT u."Id" AS user_id, entry.account, entry.position
                    FROM settings_accounts AS u
                    CROSS JOIN LATERAL (
                        SELECT value AS account, ordinality AS position
                        FROM jsonb_array_elements(
                            CASE jsonb_typeof(u."Document" -> 'MailAccounts')
                                WHEN 'array' THEN u."Document" -> 'MailAccounts'
                                WHEN 'object' THEN (
                                    SELECT coalesce(jsonb_agg(e.value ORDER BY CASE WHEN e.key ~ '^[0-9]{1,9}$' THEN e.key::int END, e.key), '[]'::jsonb)
                                    FROM jsonb_each(u."Document" -> 'MailAccounts') AS e)
                                ELSE '[]'::jsonb
                            END) WITH ORDINALITY
                    ) AS entry
                    WHERE jsonb_typeof(entry.account) = 'object'
                ),
                addressed AS (
                    SELECT gen_random_uuid() AS id, user_id, account, position,
                           nullif(btrim(CASE WHEN account ->> 'UserName' LIKE '%@%' THEN account ->> 'UserName'
                                             ELSE account #>> '{Delivery,FromAddress}' END), '') AS address
                    FROM declared
                ),
                ranked AS (
                    SELECT id, user_id, account,
                           CASE WHEN address ~ '^[^@[:space:]]+@[^@[:space:]]+$' AND length(address) <= 320
                                     AND row_number() OVER (PARTITION BY upper(address) ORDER BY user_id, position) = 1
                                THEN address END AS address
                    FROM addressed
                ),
                recorded AS (
                    INSERT INTO settings_mail_accounts
                        ("Id", "EmailAddress", "NormalizedEmailAddress", "DisplayName", "Document", "Version", "CreatedAt", "UpdatedAt")
                    SELECT id, address, upper(address),
                           left(coalesce(nullif(btrim(account ->> 'DisplayName'), ''), nullif(btrim(account ->> 'AccountId'), ''), 'Mail account'), 128),
                           account - 'AccountId' - 'DisplayName' - 'EmailAddress', 1, now(), now()
                    FROM ranked
                    RETURNING "Id"
                )
                INSERT INTO mail_account_assignments ("UserId", "MailAccountId", "AssignedAt")
                SELECT r.user_id, r.id, now()
                FROM ranked AS r
                JOIN recorded ON recorded."Id" = r.id;

                UPDATE settings_accounts
                SET "Document" = "Document" - 'MailAccounts',
                    "Version" = "Version" + 1
                WHERE "Document" ? 'MailAccounts';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Each assignment carries its account back into that user's record under the record's identifier, so an
            // account shared by several users is declared in each of their records. Mail discarded on the way up stays
            // discarded.
            migrationBuilder.Sql(
                """
                UPDATE settings_accounts AS u
                SET "Document" = jsonb_set(u."Document", '{MailAccounts}', carried.accounts),
                    "Version" = u."Version" + 1
                FROM (
                    SELECT a."UserId",
                           jsonb_agg(m."Document" || jsonb_build_object('AccountId', m."Id"::text, 'DisplayName', m."DisplayName")
                                     ORDER BY a."AssignedAt", m."Id") AS accounts
                    FROM mail_account_assignments AS a
                    JOIN settings_mail_accounts AS m ON m."Id" = a."MailAccountId"
                    GROUP BY a."UserId"
                ) AS carried
                WHERE u."Id" = carried."UserId";
                """);

            migrationBuilder.DropTable(
                name: "mail_account_assignments");

            migrationBuilder.DropTable(
                name: "settings_mail_accounts");
        }
    }
}

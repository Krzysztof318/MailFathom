// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// SQL only and no model change, deliberately: a permission is stored as its published name, in a role's list and
    /// in the list a credential or a signed-in session is narrowed to, so the schema is the same before and after.
    /// What changes is the names. Asking the assistant and the address book leave the <c>mail</c> domain for domains
    /// of their own, and what <c>mailfathom.mail.read</c> and <c>mailfathom.mail.accounts.write</c> used to reach
    /// beside mail — tasks, the calendar, notifications, and a person's profile — is published under names of its own.
    /// </para>
    /// <para>
    /// Every stored list is carried so that nobody is refused after the upgrade something the list let them do before
    /// it: a renamed name is rewritten where it stands, a list that reached <c>mailfathom.mail.read</c> gains the
    /// eight names split out of it, and one that reached <c>mailfathom.mail.accounts.write</c> gains
    /// <c>mailfathom.profile.write</c>. No other name is written into a list. A list that asked the assistant without
    /// reading mail keeps the asking name alone, because adding the reading one would hand its holder mail they were
    /// never granted.
    /// </para>
    /// <para>
    /// Two lists come out reaching more than they did, and neither is something a rewrite of names can avoid. Changing
    /// a display name is now one act of <c>mailfathom.profile.write</c> among four, so a list that held
    /// <c>mailfathom.mail.accounts.write</c> without the reading name is carried to the whole of it — the alternative
    /// is taking away the one act it had. And a pattern reaches whatever is published under it, as it does for every
    /// name a later release publishes: one written as <c>mailfathom.*.write</c> reaches the new writing names whether
    /// or not its role read mail.
    /// </para>
    /// <para>
    /// A role may list a pattern, and a pattern is carried by what it reached rather than by how it is written: the
    /// names a role held through one are written out wherever its rewritten list no longer reaches them, so
    /// <c>mailfathom.mail.*</c> goes on meaning what it meant, and a role listing <c>*</c> gains no row at all.
    /// </para>
    /// </remarks>
    public partial class SplitUserFacingPermissionsByDomain : Migration
    {
        // What a list holding the name on the left is carried to. A name that is renamed appears once; a name that is
        // split appears once per name split out of it, and stays in the list itself.
        private const string CarriedNames =
            """
            ('mailfathom.mail.ask', 'mailfathom.assistant.use'),
            ('mailfathom.mail.contacts.read', 'mailfathom.contacts.read'),
            ('mailfathom.mail.contacts.write', 'mailfathom.contacts.write'),
            ('mailfathom.mail.read', 'mailfathom.tasks.read'),
            ('mailfathom.mail.read', 'mailfathom.tasks.write'),
            ('mailfathom.mail.read', 'mailfathom.calendar.read'),
            ('mailfathom.mail.read', 'mailfathom.calendar.write'),
            ('mailfathom.mail.read', 'mailfathom.notifications.read'),
            ('mailfathom.mail.read', 'mailfathom.notifications.write'),
            ('mailfathom.mail.read', 'mailfathom.profile.read'),
            ('mailfathom.mail.read', 'mailfathom.profile.write'),
            ('mailfathom.mail.accounts.write', 'mailfathom.profile.write')
            """;

        private const string SplitOutNames =
            """
            'mailfathom.tasks.read', 'mailfathom.tasks.write', 'mailfathom.calendar.read',
            'mailfathom.calendar.write', 'mailfathom.notifications.read', 'mailfathom.notifications.write',
            'mailfathom.profile.read', 'mailfathom.profile.write'
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One statement, so that what each role held is read from the lists as they stood before any of them was
            // rewritten. A pattern is matched the way a grant is computed from one: a wildcard is a whole segment
            // standing for one or more, which is the expression each pattern is turned into below. An entry that is
            // neither a name nor a pattern is matched against nothing, exactly as it grants nothing — and it is given
            // no expression at all rather than one a condition is trusted to skip, because the order two halves of a
            // condition are evaluated in is the planner's, and an entry nobody validated need not be an expression.
            migrationBuilder.Sql(
                $$"""
                WITH carried(old_name, new_name) AS (
                    VALUES
                    {{CarriedNames}}
                ),
                listed AS (
                    SELECT entry."RoleId" AS role_id,
                           entry."Permission" AS written,
                           CASE
                               WHEN entry."Permission" = 'mailfathom.mail.ask' THEN 'mailfathom.assistant.use'
                               WHEN entry."Permission" LIKE 'mailfathom.mail.contacts.%'
                                   THEN 'mailfathom.contacts.' || substr(entry."Permission", 26)
                               ELSE entry."Permission"
                           END AS rewritten
                    FROM role_permissions AS entry
                ),
                shaped AS (
                    SELECT listed.role_id,
                           listed.written,
                           listed.rewritten,
                           listed.written ~ '^(\*|[a-z0-9_]+)(\.(\*|[a-z0-9_]+))*$' AND listed.written LIKE '%*%' AS is_pattern
                    FROM listed
                ),
                matched AS (
                    SELECT shaped.role_id,
                           shaped.written,
                           shaped.rewritten,
                           shaped.is_pattern,
                           CASE WHEN shaped.is_pattern
                               THEN '^' || replace(replace(shaped.written, '.', '\.'), '*', '[^.]+(\.[^.]+)*') || '$'
                           END AS reaches_before,
                           CASE WHEN shaped.is_pattern
                               THEN '^' || replace(replace(shaped.rewritten, '.', '\.'), '*', '[^.]+(\.[^.]+)*') || '$'
                           END AS reaches_after
                    FROM shaped
                ),
                held AS (
                    SELECT DISTINCT matched.role_id, carried.new_name
                    FROM matched
                    JOIN carried
                      ON matched.written = carried.old_name
                      OR (matched.is_pattern AND carried.old_name ~ matched.reaches_before)
                ),
                renamed AS (
                    UPDATE role_permissions AS entry
                    SET "Permission" = listed.rewritten
                    FROM listed
                    WHERE entry."RoleId" = listed.role_id
                      AND entry."Permission" = listed.written
                      AND listed.written <> listed.rewritten
                )
                INSERT INTO role_permissions ("RoleId", "Permission")
                SELECT held.role_id, held.new_name
                FROM held
                WHERE NOT EXISTS (
                    SELECT 1 FROM matched
                    WHERE matched.role_id = held.role_id
                      AND (matched.rewritten = held.new_name
                           OR (matched.is_pattern AND held.new_name ~ matched.reaches_after)))
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.Sql(CarryingANarrowingForward("user_credentials"));
            migrationBuilder.Sql(CarryingANarrowingForward("client_sessions"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The build this returns to publishes none of the split-out names, so every one of them leaves whoever
            // lists it, and the two renames are undone where they stand.
            migrationBuilder.Sql(
                $$"""
                DELETE FROM role_permissions WHERE "Permission" IN ({{SplitOutNames}});

                UPDATE role_permissions
                SET "Permission" = CASE
                        WHEN "Permission" = 'mailfathom.assistant.use' THEN 'mailfathom.mail.ask'
                        ELSE 'mailfathom.mail.contacts.' || substr("Permission", 21)
                    END
                WHERE "Permission" = 'mailfathom.assistant.use' OR "Permission" LIKE 'mailfathom.contacts.%';
                """);

            migrationBuilder.Sql(CarryingANarrowingBack("user_credentials"));
            migrationBuilder.Sql(CarryingANarrowingBack("client_sessions"));
        }

        // A narrowing names permissions exactly and never a pattern, so it is carried by equality alone. A credential
        // narrowed to nothing in particular holds no list and is left without one.
        private static string CarryingANarrowingForward(string table) =>
            $$"""
            WITH carried(old_name, new_name) AS (
                VALUES
                {{CarriedNames}}
            )
            UPDATE {{table}} AS narrowed
            SET "Permissions" = ARRAY(
                SELECT DISTINCT name
                FROM (
                    SELECT listed AS name
                    FROM unnest(narrowed."Permissions") AS listed
                    WHERE listed NOT IN (
                        'mailfathom.mail.ask', 'mailfathom.mail.contacts.read', 'mailfathom.mail.contacts.write')
                    UNION ALL
                    SELECT carried.new_name FROM carried WHERE carried.old_name = ANY (narrowed."Permissions")
                ) AS names
                ORDER BY name)
            WHERE narrowed."Permissions" && ARRAY(SELECT carried.old_name FROM carried);
            """;

        private static string CarryingANarrowingBack(string table) =>
            $$"""
            UPDATE {{table}} AS narrowed
            SET "Permissions" = ARRAY(
                SELECT DISTINCT CASE
                           WHEN listed = 'mailfathom.assistant.use' THEN 'mailfathom.mail.ask'
                           WHEN listed LIKE 'mailfathom.contacts.%' THEN 'mailfathom.mail.contacts.' || substr(listed, 21)
                           ELSE listed
                       END AS name
                FROM unnest(narrowed."Permissions") AS listed
                WHERE listed NOT IN ({{SplitOutNames}})
                ORDER BY name)
            WHERE narrowed."Permissions" && ARRAY[
                'mailfathom.assistant.use', 'mailfathom.contacts.read', 'mailfathom.contacts.write',
                {{SplitOutNames}}];
            """;
    }
}

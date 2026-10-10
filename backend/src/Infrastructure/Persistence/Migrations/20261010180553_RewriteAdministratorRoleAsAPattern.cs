// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// SQL only and no model change, deliberately: a role's list is rows of one text column, and whether an entry is a
    /// published name or a pattern is decided when a grant is computed, so the schema is the same before and after.
    /// What changes is what the seeded <c>Administrator</c> role holds on a later release: the single pattern
    /// <c>*</c> reaches everything a build publishes, so its holders come to hold a permission added afterwards
    /// without anybody writing to the role.
    /// </remarks>
    public partial class RewriteAdministratorRoleAsAPattern : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only the list the seeding migration wrote is rewritten. A role an operator has since narrowed, emptied,
            // or deleted is theirs: rewriting it would hand its holders every permission they were deliberately
            // denied, so it is left exactly as it stands.
            migrationBuilder.Sql(
                """
                WITH seeded(name) AS (
                    SELECT unnest(ARRAY[
                        'mailfathom.mail.read', 'mailfathom.mail.ask', 'mailfathom.mail.contacts.read',
                        'mailfathom.mail.contacts.write', 'mailfathom.mail.flags.write', 'mailfathom.mail.move',
                        'mailfathom.mail.delete', 'mailfathom.mail.drafts.write', 'mailfathom.mail.send',
                        'mailfathom.mail.accounts.write', 'mailfathom.mail.folders.write',
                        'mailfathom.admin.read', 'mailfathom.admin.audit.read', 'mailfathom.admin.operate',
                        'mailfathom.admin.credentials.write', 'mailfathom.admin.spend', 'mailfathom.admin.erase',
                        'mailfathom.admin.export', 'mailfathom.admin.configuration.write',
                        'mailfathom.admin.custody.write', 'mailfathom.admin.roles.write'])
                ),
                rewritten AS (
                    DELETE FROM role_permissions AS listed
                    WHERE listed."RoleId" = '01a11deb-3808-7000-8000-000000000003'
                      AND NOT EXISTS (
                          SELECT 1 FROM role_permissions AS other
                          WHERE other."RoleId" = listed."RoleId"
                            AND other."Permission" NOT IN (SELECT name FROM seeded))
                      AND (SELECT count(*) FROM role_permissions AS other WHERE other."RoleId" = listed."RoleId")
                          = (SELECT count(*) FROM seeded)
                    RETURNING listed."RoleId"
                )
                INSERT INTO role_permissions ("RoleId", "Permission")
                SELECT DISTINCT rewritten."RoleId", '*' FROM rewritten;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                WITH restored AS (
                    DELETE FROM role_permissions AS listed
                    WHERE listed."RoleId" = '01a11deb-3808-7000-8000-000000000003'
                      AND listed."Permission" = '*'
                      AND NOT EXISTS (
                          SELECT 1 FROM role_permissions AS other
                          WHERE other."RoleId" = listed."RoleId" AND other."Permission" <> '*')
                    RETURNING listed."RoleId"
                )
                INSERT INTO role_permissions ("RoleId", "Permission")
                SELECT restored."RoleId", name FROM restored CROSS JOIN unnest(ARRAY[
                    'mailfathom.mail.read', 'mailfathom.mail.ask', 'mailfathom.mail.contacts.read',
                    'mailfathom.mail.contacts.write', 'mailfathom.mail.flags.write', 'mailfathom.mail.move',
                    'mailfathom.mail.delete', 'mailfathom.mail.drafts.write', 'mailfathom.mail.send',
                    'mailfathom.mail.accounts.write', 'mailfathom.mail.folders.write',
                    'mailfathom.admin.read', 'mailfathom.admin.audit.read', 'mailfathom.admin.operate',
                    'mailfathom.admin.credentials.write', 'mailfathom.admin.spend', 'mailfathom.admin.erase',
                    'mailfathom.admin.export', 'mailfathom.admin.configuration.write',
                    'mailfathom.admin.custody.write', 'mailfathom.admin.roles.write']) AS name;
                """);
        }
    }
}

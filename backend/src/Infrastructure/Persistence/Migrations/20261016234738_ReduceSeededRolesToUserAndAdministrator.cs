// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// SQL only and no model change, deliberately: a role's name and list are rows, and whether an entry is a published
    /// name or a pattern is decided when a grant is computed, so the schema is the same before and after. What changes
    /// is which roles a database is seeded with: the one seeded as <c>Mail user</c> becomes <c>User</c> and lists one
    /// pattern per user-facing domain, so its holders come to hold what a later release publishes in those domains, and
    /// <c>Organization administrator</c> is no longer one of them. <c>Administrator</c> is untouched.
    /// </remarks>
    public partial class ReduceSeededRolesToUserAndAdministrator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only a row still as the earlier migrations left it is touched. A role an operator has since renamed,
            // narrowed, or widened is theirs: rewriting it would hand its holders what they were deliberately denied.
            // Organization administrator is removed only while nobody holds it, because removing a role somebody holds
            // takes their grant away, and the restricting foreign key refuses it anyway. User is not taken as the new
            // name where a role already carries it, since the name is unique and the upgrade must not fail over it.
            migrationBuilder.Sql(
                """
                WITH user_facing(name) AS (
                    SELECT unnest(ARRAY[
                        'mailfathom.assistant.use', 'mailfathom.calendar.read', 'mailfathom.calendar.write',
                        'mailfathom.contacts.read', 'mailfathom.contacts.write', 'mailfathom.mail.accounts.write',
                        'mailfathom.mail.delete', 'mailfathom.mail.drafts.write', 'mailfathom.mail.flags.write',
                        'mailfathom.mail.folders.write', 'mailfathom.mail.move', 'mailfathom.mail.read',
                        'mailfathom.mail.send', 'mailfathom.notifications.read', 'mailfathom.notifications.write',
                        'mailfathom.profile.read', 'mailfathom.profile.write', 'mailfathom.tasks.read',
                        'mailfathom.tasks.write'])
                ),
                organization_administration(name) AS (
                    SELECT unnest(ARRAY[
                        'mailfathom.admin.read', 'mailfathom.admin.audit.read', 'mailfathom.admin.operate',
                        'mailfathom.admin.credentials.write', 'mailfathom.admin.configuration.write',
                        'mailfathom.admin.erase', 'mailfathom.admin.roles.write'])
                ),
                untouched_user AS (
                    SELECT role."Id" FROM roles AS role
                    WHERE role."Id" = '01a11deb-3808-7000-8000-000000000001'
                      AND role."Name" = 'Mail user'
                      AND NOT EXISTS (SELECT 1 FROM roles AS other WHERE other."Name" = 'User')
                      AND NOT EXISTS (
                          SELECT 1 FROM role_permissions AS listed
                          WHERE listed."RoleId" = role."Id" AND listed."Permission" NOT IN (SELECT name FROM user_facing))
                      AND (SELECT count(*) FROM role_permissions AS listed WHERE listed."RoleId" = role."Id")
                          = (SELECT count(*) FROM user_facing)
                ),
                unheld_organization_administrator AS (
                    SELECT role."Id" FROM roles AS role
                    WHERE role."Id" = '01a11deb-3808-7000-8000-000000000002'
                      AND role."Name" = 'Organization administrator'
                      AND NOT EXISTS (SELECT 1 FROM role_assignments AS assignment WHERE assignment."RoleId" = role."Id")
                      AND NOT EXISTS (
                          SELECT 1 FROM role_permissions AS listed
                          WHERE listed."RoleId" = role."Id"
                            AND listed."Permission" NOT IN (SELECT name FROM organization_administration))
                      AND (SELECT count(*) FROM role_permissions AS listed WHERE listed."RoleId" = role."Id")
                          = (SELECT count(*) FROM organization_administration)
                ),
                renamed AS (
                    UPDATE roles SET "Name" = 'User'
                    WHERE "Id" IN (SELECT "Id" FROM untouched_user)
                    RETURNING "Id"
                ),
                cleared AS (
                    DELETE FROM role_permissions
                    WHERE "RoleId" IN (SELECT "Id" FROM untouched_user)
                    RETURNING "RoleId"
                ),
                removed AS (
                    DELETE FROM roles
                    WHERE "Id" IN (SELECT "Id" FROM unheld_organization_administrator)
                    RETURNING "Id"
                )
                INSERT INTO role_permissions ("RoleId", "Permission")
                SELECT untouched_user."Id", pattern FROM untouched_user CROSS JOIN unnest(ARRAY[
                    'mailfathom.mail.*', 'mailfathom.contacts.*', 'mailfathom.tasks.*', 'mailfathom.calendar.*',
                    'mailfathom.notifications.*', 'mailfathom.profile.*', 'mailfathom.assistant.*']) AS pattern;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                WITH user_facing_patterns(name) AS (
                    SELECT unnest(ARRAY[
                        'mailfathom.mail.*', 'mailfathom.contacts.*', 'mailfathom.tasks.*', 'mailfathom.calendar.*',
                        'mailfathom.notifications.*', 'mailfathom.profile.*', 'mailfathom.assistant.*'])
                ),
                reduced_user AS (
                    SELECT role."Id" FROM roles AS role
                    WHERE role."Id" = '01a11deb-3808-7000-8000-000000000001'
                      AND role."Name" = 'User'
                      AND NOT EXISTS (SELECT 1 FROM roles AS other WHERE other."Name" = 'Mail user')
                      AND NOT EXISTS (
                          SELECT 1 FROM role_permissions AS listed
                          WHERE listed."RoleId" = role."Id"
                            AND listed."Permission" NOT IN (SELECT name FROM user_facing_patterns))
                      AND (SELECT count(*) FROM role_permissions AS listed WHERE listed."RoleId" = role."Id")
                          = (SELECT count(*) FROM user_facing_patterns)
                ),
                renamed AS (
                    UPDATE roles SET "Name" = 'Mail user'
                    WHERE "Id" IN (SELECT "Id" FROM reduced_user)
                    RETURNING "Id"
                ),
                cleared AS (
                    DELETE FROM role_permissions
                    WHERE "RoleId" IN (SELECT "Id" FROM reduced_user)
                    RETURNING "RoleId"
                ),
                restored_organization_administrator AS (
                    INSERT INTO roles ("Id", "Name", "CreatedAt")
                    SELECT '01a11deb-3808-7000-8000-000000000002', 'Organization administrator', now()
                    WHERE NOT EXISTS (
                        SELECT 1 FROM roles AS other
                        WHERE other."Id" = '01a11deb-3808-7000-8000-000000000002'
                           OR other."Name" = 'Organization administrator')
                    RETURNING "Id"
                )
                INSERT INTO role_permissions ("RoleId", "Permission")
                SELECT reduced_user."Id", name FROM reduced_user CROSS JOIN unnest(ARRAY[
                    'mailfathom.assistant.use', 'mailfathom.calendar.read', 'mailfathom.calendar.write',
                    'mailfathom.contacts.read', 'mailfathom.contacts.write', 'mailfathom.mail.accounts.write',
                    'mailfathom.mail.delete', 'mailfathom.mail.drafts.write', 'mailfathom.mail.flags.write',
                    'mailfathom.mail.folders.write', 'mailfathom.mail.move', 'mailfathom.mail.read',
                    'mailfathom.mail.send', 'mailfathom.notifications.read', 'mailfathom.notifications.write',
                    'mailfathom.profile.read', 'mailfathom.profile.write', 'mailfathom.tasks.read',
                    'mailfathom.tasks.write']) AS name
                UNION ALL
                SELECT restored_organization_administrator."Id", name FROM restored_organization_administrator
                CROSS JOIN unnest(ARRAY[
                    'mailfathom.admin.read', 'mailfathom.admin.audit.read', 'mailfathom.admin.operate',
                    'mailfathom.admin.credentials.write', 'mailfathom.admin.configuration.write',
                    'mailfathom.admin.erase', 'mailfathom.admin.roles.write']) AS name;
                """);
        }
    }
}

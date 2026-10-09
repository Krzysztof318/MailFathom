// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HoldMailGrantsOnUsers : Migration
    {
        // Moves what a caller may do from each credential's list onto its user, without any caller gaining or losing a
        // permission on the day it runs. Before it, a credential's list was the grant, and an unwritten one was written
        // out as the whole mail half on the day the credential was provisioned; after it, a user holds what their role
        // assignments grant and a credential's list only narrows that.
        //
        // So every user who holds no assignment yet is given one at their own scope reproducing the union of what their
        // credentials listed: the seeded Mail user role where the union is the whole mail half, and otherwise a role of
        // its own per distinct union, since a role is the only thing an assignment can name. A user holding no
        // credential is given the whole mail half, because the only caller that could have acted for them is one an
        // endpoint requiring no credential admitted, and that caller held the whole half. A user whose credentials all
        // listed nothing is given nothing. A user who already holds an assignment is left as somebody decided.
        //
        // Every credential listing the whole mail half then records no narrowing at all, which is how a credential
        // provisioned without one was meant: it holds what its user holds, so a permission a later release publishes
        // reaches it once a role carries it. Under the assignment above that is exactly the half it listed, so nothing
        // changes on the day. A credential listing less keeps its list as its narrowing, and holds exactly that list.
        //
        // The mail half is the eleven names as this release publishes them, stated literally rather than read from the
        // build, for the reason the seeding migration gives. The statements share temporary tables and therefore this
        // migration's transaction; the role-writing one names no seeded role, which is the rule seeded roles are held to.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string[]>(
                name: "Permissions",
                table: "user_credentials",
                type: "text[]",
                nullable: true,
                oldClrType: typeof(string[]),
                oldType: "text[]");

            migrationBuilder.Sql(
                """
                CREATE TEMPORARY TABLE carried_mail_grants ON COMMIT DROP AS
                SELECT account."Id" AS user_id,
                       ARRAY(
                           SELECT mail.name
                           FROM unnest(ARRAY[
                    'mailfathom.mail.read', 'mailfathom.mail.ask', 'mailfathom.mail.contacts.read',
                    'mailfathom.mail.contacts.write', 'mailfathom.mail.flags.write', 'mailfathom.mail.move',
                    'mailfathom.mail.delete', 'mailfathom.mail.drafts.write', 'mailfathom.mail.send',
                    'mailfathom.mail.accounts.write', 'mailfathom.mail.folders.write']::text[]) WITH ORDINALITY AS mail(name, position)
                           WHERE NOT EXISTS (SELECT 1 FROM user_credentials held WHERE held."UserId" = account."Id")
                              OR EXISTS (
                                  SELECT 1 FROM user_credentials held
                                  WHERE held."UserId" = account."Id" AND mail.name = ANY (held."Permissions"))
                           ORDER BY mail.position) AS names
                FROM settings_accounts account
                WHERE NOT EXISTS (
                    SELECT 1 FROM role_assignments assigned WHERE assigned."PrincipalUserId" = account."Id");

                CREATE TEMPORARY TABLE carried_mail_roles ON COMMIT DROP AS
                SELECT gen_random_uuid() AS role_id,
                       'Carried-over mail grant ' || row_number() OVER (ORDER BY array_to_string(names, ',')) AS role_name,
                       names
                FROM (
                    SELECT DISTINCT names FROM carried_mail_grants
                    WHERE cardinality(names) BETWEEN 1 AND 10) AS partial_grants;
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO roles ("Id", "Name", "CreatedAt")
                SELECT role_id, role_name, now() FROM carried_mail_roles;

                INSERT INTO role_permissions ("RoleId", "Permission")
                SELECT carried.role_id, name FROM carried_mail_roles carried, unnest(carried.names) AS name;
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO role_assignments ("Id", "RoleId", "PrincipalUserId", "ScopeUserId", "AssignedAt")
                SELECT gen_random_uuid(), '01a11deb-3808-7000-8000-000000000001'::uuid, carried.user_id, carried.user_id, now()
                FROM carried_mail_grants carried
                WHERE cardinality(carried.names) = 11
                UNION ALL
                SELECT gen_random_uuid(), carried_role.role_id, carried.user_id, carried.user_id, now()
                FROM carried_mail_grants carried
                JOIN carried_mail_roles carried_role ON carried_role.names = carried.names;

                UPDATE user_credentials SET "Permissions" = NULL
                WHERE "Permissions" @> ARRAY[
                    'mailfathom.mail.read', 'mailfathom.mail.ask', 'mailfathom.mail.contacts.read',
                    'mailfathom.mail.contacts.write', 'mailfathom.mail.flags.write', 'mailfathom.mail.move',
                    'mailfathom.mail.delete', 'mailfathom.mail.drafts.write', 'mailfathom.mail.send',
                    'mailfathom.mail.accounts.write', 'mailfathom.mail.folders.write']::text[];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A credential naming nothing goes back to listing the whole mail half, which is what it held under the
            // previous release; the roles this migration created go with their assignments. The Mail user assignments it
            // wrote are left, since nothing tells them from ones an operator wrote and the earlier release reads none.
            migrationBuilder.Sql(
                """
                UPDATE user_credentials SET "Permissions" = ARRAY[
                    'mailfathom.mail.read', 'mailfathom.mail.ask', 'mailfathom.mail.contacts.read',
                    'mailfathom.mail.contacts.write', 'mailfathom.mail.flags.write', 'mailfathom.mail.move',
                    'mailfathom.mail.delete', 'mailfathom.mail.drafts.write', 'mailfathom.mail.send',
                    'mailfathom.mail.accounts.write', 'mailfathom.mail.folders.write']::text[]
                WHERE "Permissions" IS NULL;

                DELETE FROM role_assignments
                WHERE "RoleId" IN (SELECT "Id" FROM roles WHERE "Name" LIKE 'Carried-over mail grant %');

                DELETE FROM roles WHERE "Name" LIKE 'Carried-over mail grant %';
                """);

            migrationBuilder.AlterColumn<string[]>(
                name: "Permissions",
                table: "user_credentials",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0],
                oldClrType: typeof(string[]),
                oldType: "text[]",
                oldNullable: true);
        }
    }
}

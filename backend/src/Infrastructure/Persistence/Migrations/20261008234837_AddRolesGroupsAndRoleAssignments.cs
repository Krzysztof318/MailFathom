// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRolesGroupsAndRoleAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_groups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_groups_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Permission = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permissions", x => new { x.RoleId, x.Permission });
                    table.ForeignKey(
                        name: "FK_role_permissions_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrincipalUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PrincipalGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScopeOrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScopeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_assignments", x => x.Id);
                    table.CheckConstraint("ck_role_assignments_at_most_one_scope", "num_nonnulls(\"ScopeOrganizationId\", \"ScopeUserId\") <= 1");
                    table.CheckConstraint("ck_role_assignments_one_principal", "num_nonnulls(\"PrincipalUserId\", \"PrincipalGroupId\") = 1");
                    table.ForeignKey(
                        name: "fk_role_assignments_principal_group",
                        column: x => x.PrincipalGroupId,
                        principalTable: "user_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_role_assignments_principal_user",
                        column: x => x.PrincipalUserId,
                        principalTable: "settings_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_role_assignments_roles",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_role_assignments_scope_organization",
                        column: x => x.ScopeOrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_role_assignments_scope_user",
                        column: x => x.ScopeUserId,
                        principalTable: "settings_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_group_members",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_group_members", x => new { x.GroupId, x.UserId });
                    table.ForeignKey(
                        name: "FK_user_group_members_settings_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "settings_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_group_members_user_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "user_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_role_assignments_PrincipalGroupId",
                table: "role_assignments",
                column: "PrincipalGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_role_assignments_PrincipalUserId",
                table: "role_assignments",
                column: "PrincipalUserId");

            migrationBuilder.CreateIndex(
                name: "ix_role_assignments_role_principal_scope",
                table: "role_assignments",
                columns: new[] { "RoleId", "PrincipalUserId", "PrincipalGroupId", "ScopeOrganizationId", "ScopeUserId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_role_assignments_ScopeOrganizationId",
                table: "role_assignments",
                column: "ScopeOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_role_assignments_ScopeUserId",
                table: "role_assignments",
                column: "ScopeUserId");

            migrationBuilder.CreateIndex(
                name: "ix_roles_name",
                table: "roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_group_members_UserId",
                table: "user_group_members",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "ix_user_groups_name",
                table: "user_groups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_groups_OrganizationId",
                table: "user_groups",
                column: "OrganizationId");

            // The built-in roles, written once as ordinary rows an operator may rename, edit, or delete. Each list is
            // the names as this release publishes them and is stated literally rather than read from the build, so a
            // later release that publishes a permission adds it to none of them and no later migration edits them.
            // mailfathom.admin.roles.write is published by the routes that administer these records, in the same
            // release; until then it is read back as unpublished and grants nothing.
            migrationBuilder.Sql(
                """
                INSERT INTO roles ("Id", "Name", "CreatedAt") VALUES
                    ('01a11deb-3808-7000-8000-000000000001', 'Mail user', now()),
                    ('01a11deb-3808-7000-8000-000000000002', 'Organization administrator', now()),
                    ('01a11deb-3808-7000-8000-000000000003', 'Administrator', now());

                INSERT INTO role_permissions ("RoleId", "Permission")
                SELECT '01a11deb-3808-7000-8000-000000000001'::uuid, name FROM unnest(ARRAY[
                    'mailfathom.mail.read', 'mailfathom.mail.ask', 'mailfathom.mail.contacts.read',
                    'mailfathom.mail.contacts.write', 'mailfathom.mail.flags.write', 'mailfathom.mail.move',
                    'mailfathom.mail.delete', 'mailfathom.mail.drafts.write', 'mailfathom.mail.send',
                    'mailfathom.mail.accounts.write', 'mailfathom.mail.folders.write']) AS name
                UNION ALL
                SELECT '01a11deb-3808-7000-8000-000000000002'::uuid, name FROM unnest(ARRAY[
                    'mailfathom.admin.read', 'mailfathom.admin.audit.read', 'mailfathom.admin.operate',
                    'mailfathom.admin.credentials.write', 'mailfathom.admin.configuration.write',
                    'mailfathom.admin.erase', 'mailfathom.admin.roles.write']) AS name
                UNION ALL
                SELECT '01a11deb-3808-7000-8000-000000000003'::uuid, name FROM unnest(ARRAY[
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_assignments");

            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "user_group_members");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "user_groups");
        }
    }
}

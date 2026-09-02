// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    // Authored against a base that did not yet carry 20260815201928_AddStoredEmailSenderAuthentication, so this
    // identifier sorts before a migration that merged first and is never the newest one a filename sort finds. It is
    // permanent, because a database has already written it into __EFMigrationsHistory, and the ordering costs nothing
    // at runtime: both migrations only add columns and indexes to stored_emails, and either order leaves one schema.
    // What it does cost is scripts/script-migration.sh with no arguments, which reads that sort and would script the
    // other migration. Review this one's SQL by naming the pair:
    //   scripts/script-migration.sh 20260813161625_AddMailRuleScheduleTrigger 20260815165050_AddStoredEmailRemoteKeywords

    /// <inheritdoc />
    public partial class AddStoredEmailRemoteKeywords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "RemoteKeywords",
                table: "stored_emails",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_remote_keywords",
                table: "stored_emails",
                column: "RemoteKeywords")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stored_emails_remote_keywords",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "RemoteKeywords",
                table: "stored_emails");
        }
    }
}

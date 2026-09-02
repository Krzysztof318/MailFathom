// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpamClassificationRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Profile",
                table: "email_spam_classifications",
                type: "character(12)",
                fixedLength: true,
                maxLength: 12,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "spam_classification_runs",
                columns: table => new
                {
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FolderAliases = table.Column<string[]>(type: "text[]", nullable: false),
                    Posture = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Rescores = table.Column<bool>(type: "boolean", nullable: false),
                    Profile = table.Column<string>(type: "character(12)", fixedLength: true, maxLength: 12, nullable: true),
                    Position = table.Column<Guid>(type: "uuid", nullable: true),
                    ClassifiedEmailCount = table.Column<int>(type: "integer", nullable: false),
                    SpamEmailCount = table.Column<int>(type: "integer", nullable: false),
                    UndeterminedEmailCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedEmailCount = table.Column<int>(type: "integer", nullable: false),
                    UnclassifiableEmailCount = table.Column<int>(type: "integer", nullable: false),
                    ActedEmailCount = table.Column<int>(type: "integer", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Ending = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spam_classification_runs", x => x.MailboxAccountId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "spam_classification_runs");

            migrationBuilder.DropColumn(
                name: "Profile",
                table: "email_spam_classifications");
        }
    }
}

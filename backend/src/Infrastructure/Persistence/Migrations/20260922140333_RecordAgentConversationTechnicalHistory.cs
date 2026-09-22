// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordAgentConversationTechnicalHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "VisibleEntryCount",
                table: "agent_conversations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "Visible",
                table: "agent_conversation_entries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Every entry written before this migration is one a person reads, because the technical kinds arrive with
            // it, so each existing row is visible and each conversation's visible count is the count it already holds.
            migrationBuilder.Sql("""UPDATE agent_conversation_entries SET "Visible" = TRUE;""");
            migrationBuilder.Sql("""UPDATE agent_conversations SET "VisibleEntryCount" = "Sequence";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VisibleEntryCount",
                table: "agent_conversations");

            migrationBuilder.DropColumn(
                name: "Visible",
                table: "agent_conversation_entries");
        }
    }
}

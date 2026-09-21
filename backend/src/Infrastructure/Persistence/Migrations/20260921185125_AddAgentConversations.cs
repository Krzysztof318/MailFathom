// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    ComposingMessageId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_conversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_conversations_settings_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "settings_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_conversation_entries",
                columns: table => new
                {
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "json", nullable: false),
                    AnsweredProposalAt = table.Column<long>(type: "bigint", nullable: true),
                    ProposalState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    WrittenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_conversation_entries", x => new { x.ConversationId, x.Sequence });
                    table.ForeignKey(
                        name: "FK_agent_conversation_entries_agent_conversations_Conversation~",
                        column: x => x.ConversationId,
                        principalTable: "agent_conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_conversation_entries_answered",
                table: "agent_conversation_entries",
                columns: new[] { "ConversationId", "AnsweredProposalAt" },
                filter: "\"AnsweredProposalAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_agent_conversations_user_last_activity",
                table: "agent_conversations",
                columns: new[] { "UserId", "LastActivityAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_conversation_entries");

            migrationBuilder.DropTable(
                name: "agent_conversations");
        }
    }
}

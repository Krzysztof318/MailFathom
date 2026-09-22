// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;
using Pgvector;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndexAgentConversationsForHistorySearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "agent_conversation_entries",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "CASE WHEN \"Kind\" = 'message' THEN to_tsvector('simple'::regconfig, coalesce(\"Payload\" ->> 'text', '')) WHEN \"Kind\" IN ('block', 'proposal') THEN json_to_tsvector('simple'::regconfig, \"Payload\" -> 'block', '[\"string\"]') END",
                stored: true);

            migrationBuilder.CreateTable(
                name: "agent_conversation_embeddings",
                columns: table => new
                {
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    EmbeddingProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Dimension = table.Column<int>(type: "integer", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_conversation_embeddings", x => new { x.ConversationId, x.Sequence, x.EmbeddingProfileId });
                    table.CheckConstraint("ck_agent_conversation_embeddings_dimension", "vector_dims(\"Embedding\") = \"Dimension\"");
                    table.ForeignKey(
                        name: "fk_agent_conversation_embeddings_embedding_profiles",
                        columns: x => new { x.EmbeddingProfileId, x.Dimension },
                        principalTable: "embedding_profiles",
                        principalColumns: new[] { "Id", "Dimension" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_conversation_embeddings_entries",
                        columns: x => new { x.ConversationId, x.Sequence },
                        principalTable: "agent_conversation_entries",
                        principalColumns: new[] { "ConversationId", "Sequence" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_conversation_entries_search_vector",
                table: "agent_conversation_entries",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "ix_agent_conversation_embeddings_profile",
                table: "agent_conversation_embeddings",
                columns: new[] { "EmbeddingProfileId", "Dimension" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_conversation_embeddings");

            migrationBuilder.DropIndex(
                name: "ix_agent_conversation_entries_search_vector",
                table: "agent_conversation_entries");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "agent_conversation_entries");
        }
    }
}

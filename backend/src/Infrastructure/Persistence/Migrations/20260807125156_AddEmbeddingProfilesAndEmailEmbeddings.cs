// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbeddingProfilesAndEmailEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "embedding_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModelIdentifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Dimension = table.Column<int>(type: "integer", nullable: false),
                    DistanceMetric = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InputCharacterLimit = table.Column<int>(type: "integer", nullable: false),
                    PassageInstruction = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    NormalizesVector = table.Column<bool>(type: "boolean", nullable: false),
                    IdentityFingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    LifecycleState = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SupersededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_embedding_profiles", x => x.Id);
                    table.UniqueConstraint("ak_embedding_profiles_id_dimension", x => new { x.Id, x.Dimension });
                });

            migrationBuilder.CreateTable(
                name: "email_embeddings",
                columns: table => new
                {
                    EmailChunkId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmbeddingProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Dimension = table.Column<int>(type: "integer", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_embeddings", x => new { x.EmailChunkId, x.EmbeddingProfileId });
                    table.CheckConstraint("ck_email_embeddings_dimension", "vector_dims(\"Embedding\") = \"Dimension\"");
                    table.ForeignKey(
                        name: "FK_email_embeddings_email_chunks_EmailChunkId",
                        column: x => x.EmailChunkId,
                        principalTable: "email_chunks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_email_embeddings_embedding_profiles",
                        columns: x => new { x.EmbeddingProfileId, x.Dimension },
                        principalTable: "embedding_profiles",
                        principalColumns: new[] { "Id", "Dimension" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_embeddings_profile",
                table: "email_embeddings",
                columns: new[] { "EmbeddingProfileId", "Dimension" });

            migrationBuilder.CreateIndex(
                name: "ix_embedding_profiles_identity_fingerprint",
                table: "embedding_profiles",
                column: "IdentityFingerprint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_embeddings");

            migrationBuilder.DropTable(
                name: "embedding_profiles");
        }
    }
}

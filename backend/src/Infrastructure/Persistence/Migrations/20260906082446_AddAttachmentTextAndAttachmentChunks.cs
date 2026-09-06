// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttachmentTextAndAttachmentChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_email_chunks_email_ordinal",
                table: "email_chunks");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AttachmentTextDerivedAt",
                table: "stored_emails",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AttachmentPosition",
                table: "email_chunks",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "email_attachment_texts",
                columns: table => new
                {
                    StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttachmentPosition = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeclaredMediaType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Text = table.Column<string>(type: "text", nullable: true),
                    PageCount = table.Column<int>(type: "integer", nullable: false),
                    Segments = table.Column<string>(type: "jsonb", nullable: true),
                    DerivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SensitiveContentStamp = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "CASE WHEN \"Kind\" = 'Document' THEN to_tsvector('simple'::regconfig, coalesce(\"FileName\", '') || ' ' || coalesce(\"Text\", '')) END", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_attachment_texts", x => new { x.StoredEmailId, x.AttachmentPosition });
                    table.ForeignKey(
                        name: "FK_email_attachment_texts_stored_emails_StoredEmailId",
                        column: x => x.StoredEmailId,
                        principalTable: "stored_emails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_chunks_email_attachment_ordinal",
                table: "email_chunks",
                columns: new[] { "StoredEmailId", "AttachmentPosition", "Ordinal" },
                unique: true,
                filter: "\"AttachmentPosition\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_email_chunks_email_ordinal",
                table: "email_chunks",
                columns: new[] { "StoredEmailId", "Ordinal" },
                unique: true,
                filter: "\"AttachmentPosition\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_email_attachment_texts_search_vector",
                table: "email_attachment_texts",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_attachment_texts");

            migrationBuilder.DropIndex(
                name: "ix_email_chunks_email_attachment_ordinal",
                table: "email_chunks");

            migrationBuilder.DropIndex(
                name: "ix_email_chunks_email_ordinal",
                table: "email_chunks");

            migrationBuilder.DropColumn(
                name: "AttachmentTextDerivedAt",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "AttachmentPosition",
                table: "email_chunks");

            migrationBuilder.CreateIndex(
                name: "ix_email_chunks_email_ordinal",
                table: "email_chunks",
                columns: new[] { "StoredEmailId", "Ordinal" },
                unique: true);
        }
    }
}

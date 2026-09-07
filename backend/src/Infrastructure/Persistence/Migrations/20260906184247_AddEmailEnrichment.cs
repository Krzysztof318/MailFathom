// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailEnrichment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_enrichments",
                columns: table => new
                {
                    StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    DerivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_enrichments", x => x.StoredEmailId);
                    table.ForeignKey(
                        name: "FK_email_enrichments_stored_emails_StoredEmailId",
                        column: x => x.StoredEmailId,
                        principalTable: "stored_emails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "email_enrichment_marks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    Aspect = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Text = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Reason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Origin = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Evidence = table.Column<Guid[]>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_enrichment_marks", x => x.Id);
                    table.ForeignKey(
                        name: "fk_email_enrichment_marks_enrichments",
                        column: x => x.StoredEmailId,
                        principalTable: "email_enrichments",
                        principalColumn: "StoredEmailId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_enrichment_marks_enrichment_aspect",
                table: "email_enrichment_marks",
                columns: new[] { "StoredEmailId", "Aspect" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_enrichment_marks");

            migrationBuilder.DropTable(
                name: "email_enrichments");
        }
    }
}

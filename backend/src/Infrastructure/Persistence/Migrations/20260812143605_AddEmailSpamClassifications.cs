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
    public partial class AddEmailSpamClassifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_spam_classifications",
                columns: table => new
                {
                    StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    Verdict = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DecidedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: true),
                    Threshold = table.Column<double>(type: "double precision", nullable: true),
                    CorpusRevision = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_spam_classifications", x => x.StoredEmailId);
                    table.ForeignKey(
                        name: "FK_email_spam_classifications_stored_emails_StoredEmailId",
                        column: x => x.StoredEmailId,
                        principalTable: "stored_emails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "email_spam_classification_signals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Observation = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Origin = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_spam_classification_signals", x => x.Id);
                    table.ForeignKey(
                        name: "fk_email_spam_classification_signals_classifications",
                        column: x => x.StoredEmailId,
                        principalTable: "email_spam_classifications",
                        principalColumn: "StoredEmailId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_spam_classification_signals_classification_ordinal",
                table: "email_spam_classification_signals",
                columns: new[] { "StoredEmailId", "Ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_spam_classification_signals");

            migrationBuilder.DropTable(
                name: "email_spam_classifications");
        }
    }
}

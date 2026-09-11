// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeploymentWideCeilingRows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mail_answering_spend_periods",
                columns: table => new
                {
                    PeriodStartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AdmittedRunCount = table.Column<int>(type: "integer", nullable: false),
                    ConsumedTokenCount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mail_answering_spend_periods", x => x.PeriodStartsAt);
                });

            migrationBuilder.CreateTable(
                name: "provider_pace_markers",
                columns: table => new
                {
                    Workload = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NextSlotAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_pace_markers", x => x.Workload);
                });

            migrationBuilder.CreateTable(
                name: "stored_content_claims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimedByteCount = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stored_content_claims", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stored_content_claims_ExpiresAt",
                table: "stored_content_claims",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mail_answering_spend_periods");

            migrationBuilder.DropTable(
                name: "provider_pace_markers");

            migrationBuilder.DropTable(
                name: "stored_content_claims");
        }
    }
}

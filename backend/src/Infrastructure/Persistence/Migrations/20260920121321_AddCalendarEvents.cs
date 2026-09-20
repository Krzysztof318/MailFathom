// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "calendar_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceStoredEmailId = table.Column<Guid>(type: "uuid", nullable: true),
                    ImportedUid = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AmendedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calendar_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_calendar_events_settings_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "settings_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_calendar_events_stored_emails_SourceStoredEmailId",
                        column: x => x.SourceStoredEmailId,
                        principalTable: "stored_emails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_calendar_events_SourceStoredEmailId",
                table: "calendar_events",
                column: "SourceStoredEmailId");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_events_user_imported_uid",
                table: "calendar_events",
                columns: new[] { "UserId", "ImportedUid" },
                unique: true,
                filter: "\"ImportedUid\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_events_user_starts_at_id",
                table: "calendar_events",
                columns: new[] { "UserId", "StartsAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calendar_events");
        }
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarEventReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TargetCalendarEventId",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAllDay",
                table: "calendar_events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "calendar_event_reminders",
                columns: table => new
                {
                    CalendarEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    MinutesBefore = table.Column<int>(type: "integer", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RaisedForDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calendar_event_reminders", x => new { x.CalendarEventId, x.MinutesBefore });
                    table.ForeignKey(
                        name: "FK_calendar_event_reminders_calendar_events_CalendarEventId",
                        column: x => x.CalendarEventId,
                        principalTable: "calendar_events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_TargetCalendarEventId",
                table: "notifications",
                column: "TargetCalendarEventId");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_reminders_due_at",
                table: "calendar_event_reminders",
                column: "DueAt",
                filter: "\"RaisedForDueAt\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_notifications_calendar_events_TargetCalendarEventId",
                table: "notifications",
                column: "TargetCalendarEventId",
                principalTable: "calendar_events",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notifications_calendar_events_TargetCalendarEventId",
                table: "notifications");

            migrationBuilder.DropTable(
                name: "calendar_event_reminders");

            migrationBuilder.DropIndex(
                name: "IX_notifications_TargetCalendarEventId",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "TargetCalendarEventId",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "IsAllDay",
                table: "calendar_events");
        }
    }
}

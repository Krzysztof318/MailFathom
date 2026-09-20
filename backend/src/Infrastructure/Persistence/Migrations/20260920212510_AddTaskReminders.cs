// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DueDayOffsetMinutes",
                table: "tasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetPersonalTaskId",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "task_reminders",
                columns: table => new
                {
                    PersonalTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    MinutesBefore = table.Column<int>(type: "integer", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RaisedForDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_reminders", x => new { x.PersonalTaskId, x.MinutesBefore });
                    table.ForeignKey(
                        name: "FK_task_reminders_tasks_PersonalTaskId",
                        column: x => x.PersonalTaskId,
                        principalTable: "tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_TargetPersonalTaskId",
                table: "notifications",
                column: "TargetPersonalTaskId");

            migrationBuilder.CreateIndex(
                name: "ix_task_reminders_due_at",
                table: "task_reminders",
                column: "DueAt",
                filter: "\"RaisedForDueAt\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_notifications_tasks_TargetPersonalTaskId",
                table: "notifications",
                column: "TargetPersonalTaskId",
                principalTable: "tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notifications_tasks_TargetPersonalTaskId",
                table: "notifications");

            migrationBuilder.DropTable(
                name: "task_reminders");

            migrationBuilder.DropIndex(
                name: "IX_notifications_TargetPersonalTaskId",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "DueDayOffsetMinutes",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "TargetPersonalTaskId",
                table: "notifications");
        }
    }
}

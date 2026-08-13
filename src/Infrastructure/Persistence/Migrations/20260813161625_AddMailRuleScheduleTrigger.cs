// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMailRuleScheduleTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Trigger",
                table: "mail_rule_evaluation_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "RequestedRun");

            migrationBuilder.CreateTable(
                name: "job_schedules",
                columns: table => new
                {
                    ScheduleId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ObservedFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastOccurrenceAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastDispatchedJobId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_schedules", x => x.ScheduleId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_schedules");

            migrationBuilder.DropColumn(
                name: "Trigger",
                table: "mail_rule_evaluation_runs");
        }
    }
}

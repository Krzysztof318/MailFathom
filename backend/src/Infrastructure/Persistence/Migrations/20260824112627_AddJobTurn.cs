// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobTurn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_jobs_claimable",
                table: "jobs");

            // Added nullable and filled rather than defaulted, because the default EF composes for a non-null instant
            // is the minimum one, which Npgsql writes as -infinity: every job this deployment already had waiting
            // would hold a turn before every job it will ever enqueue, and the queue would drain its backlog in the
            // order it was going to drain it anyway.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TurnAt",
                table: "jobs",
                type: "timestamp with time zone",
                nullable: true);

            // The instant a job became available is the turn it already held: before this migration that column was
            // the whole of the claim's order, so filling it this way is the queue keeping the order it has rather than
            // being reshuffled by an upgrade. Fairness begins with what is enqueued afterwards.
            migrationBuilder.Sql(
                """
                UPDATE jobs
                SET "TurnAt" = "AvailableAt"
                WHERE "TurnAt" IS NULL;
                """);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "TurnAt",
                table: "jobs",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_jobs_account_turn",
                table: "jobs",
                columns: new[] { "MailboxAccountId", "TurnAt" },
                filter: "\"State\" IN ('Pending', 'Claimed')");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_claimable",
                table: "jobs",
                columns: new[] { "JobType", "TurnAt" },
                filter: "\"State\" IN ('Pending', 'Claimed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_jobs_account_turn",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "ix_jobs_claimable",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "TurnAt",
                table: "jobs");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_claimable",
                table: "jobs",
                columns: new[] { "JobType", "AvailableAt" },
                filter: "\"State\" IN ('Pending', 'Claimed')");
        }
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMailRuleEvaluationState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RulesEvaluatedAt",
                table: "stored_emails",
                type: "timestamp with time zone",
                nullable: true);

            // Every message already stored is stamped as evaluated, and the stamp is the point of the column rather
            // than a tidying of it. The arrival queue is "rows carrying no value", so without this an upgrade would
            // hand the deployment's first rule set an entire mailbox's history as though it had all just arrived —
            // which, once a match leads to a change to the mail, is a mass filing nobody asked for. Running the rules
            // over mail already stored stays available as the whole-mailbox run an owner requests deliberately.
            // One statement over the table at upgrade time, before the partial index below is built over the result.
            migrationBuilder.Sql("UPDATE stored_emails SET \"RulesEvaluatedAt\" = now() WHERE \"RulesEvaluatedAt\" IS NULL;");

            migrationBuilder.CreateTable(
                name: "mail_rule_evaluation_runs",
                columns: table => new
                {
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<string>(type: "character(12)", fixedLength: true, maxLength: 12, nullable: true),
                    Position = table.Column<Guid>(type: "uuid", nullable: true),
                    EvaluatedEmailCount = table.Column<int>(type: "integer", nullable: false),
                    MatchedEmailCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedEmailCount = table.Column<int>(type: "integer", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Ending = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mail_rule_evaluation_runs", x => x.MailboxAccountId);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_account_identity",
                table: "stored_emails",
                columns: new[] { "MailboxAccountId", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_awaiting_rule_evaluation",
                table: "stored_emails",
                columns: new[] { "MailboxAccountId", "Id" },
                filter: "\"RulesEvaluatedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mail_rule_evaluation_runs");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_account_identity",
                table: "stored_emails");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_awaiting_rule_evaluation",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "RulesEvaluatedAt",
                table: "stored_emails");
        }
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMailRuleExecutionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mail_rule_executions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<string>(type: "character(12)", fixedLength: true, maxLength: 12, nullable: false),
                    Trigger = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConditionFailure = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReadFacts = table.Column<string[]>(type: "text[]", nullable: false),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "interval", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mail_rule_executions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mail_rule_executions_stored_emails_StoredEmailId",
                        column: x => x.StoredEmailId,
                        principalTable: "stored_emails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mail_rule_executed_actions",
                columns: table => new
                {
                    MailRuleExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Mutation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DestinationAlias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MutationRecordId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mail_rule_executed_actions", x => new { x.MailRuleExecutionId, x.Position });
                    table.ForeignKey(
                        name: "FK_mail_rule_executed_actions_mail_rule_executions_MailRuleExe~",
                        column: x => x.MailRuleExecutionId,
                        principalTable: "mail_rule_executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mail_rule_executions_account_evaluated",
                table: "mail_rule_executions",
                columns: new[] { "MailboxAccountId", "EvaluatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_rule_executions_account_rule_evaluated",
                table: "mail_rule_executions",
                columns: new[] { "MailboxAccountId", "RuleName", "EvaluatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_rule_executions_email_evaluated",
                table: "mail_rule_executions",
                columns: new[] { "StoredEmailId", "EvaluatedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mail_rule_executed_actions");

            migrationBuilder.DropTable(
                name: "mail_rule_executions");
        }
    }
}

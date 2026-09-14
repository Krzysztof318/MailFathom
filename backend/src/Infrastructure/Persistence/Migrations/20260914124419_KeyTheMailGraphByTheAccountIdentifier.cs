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
    public partial class KeyTheMailGraphByTheAccountIdentifier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_email_threads_mailbox_accounts_UserId_MailboxAccountId",
                table: "email_threads");

            migrationBuilder.DropForeignKey(
                name: "FK_jobs_mailbox_accounts_UserId_MailboxAccountId",
                table: "jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_local_mail_folders_mailbox_accounts_UserId_MailboxAccountId",
                table: "local_mail_folders");

            migrationBuilder.DropForeignKey(
                name: "FK_mail_folders_mailbox_accounts_UserId_MailboxAccountId",
                table: "mail_folders");

            migrationBuilder.DropForeignKey(
                name: "FK_mailbox_accounts_settings_accounts_UserId",
                table: "mailbox_accounts");

            migrationBuilder.DropTable(
                name: "user_stored_content");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_awaiting_attachment_text",
                table: "stored_emails");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_awaiting_rule_evaluation",
                table: "stored_emails");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_filed_sent_copy",
                table: "stored_emails");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_user_account_identity",
                table: "stored_emails");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_user_account_timeline",
                table: "stored_emails");

            migrationBuilder.DropPrimaryKey(
                name: "pk_spam_classification_runs",
                table: "spam_classification_runs");

            migrationBuilder.DropIndex(
                name: "ix_recurring_sends_identity",
                table: "recurring_sends");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_emails_claimable",
                table: "outgoing_emails");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_emails_identity",
                table: "outgoing_emails");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_emails_outstanding",
                table: "outgoing_emails");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_emails_period_usage",
                table: "outgoing_emails");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_email_filings_message_id",
                table: "outgoing_email_filings");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_email_filings_placement",
                table: "outgoing_email_filings");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_email_filings_recent_by_filing",
                table: "outgoing_email_filings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_mailbox_refresh_tokens",
                table: "mailbox_refresh_tokens");

            migrationBuilder.DropIndex(
                name: "ix_mailbox_mutations_outstanding",
                table: "mailbox_mutations");

            migrationBuilder.DropIndex(
                name: "ix_mailbox_mutations_placement",
                table: "mailbox_mutations");

            migrationBuilder.DropIndex(
                name: "ix_mailbox_mutation_audit_entries_user_account_completed",
                table: "mailbox_mutation_audit_entries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_mailbox_accounts",
                table: "mailbox_accounts");

            migrationBuilder.DropIndex(
                name: "ix_mail_rule_executions_user_account_evaluated",
                table: "mail_rule_executions");

            migrationBuilder.DropIndex(
                name: "ix_mail_rule_executions_user_account_rule_evaluated",
                table: "mail_rule_executions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mail_rule_evaluation_runs",
                table: "mail_rule_evaluation_runs");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mail_rederivation_runs",
                table: "mail_rederivation_runs");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mail_rederivation_positions",
                table: "mail_rederivation_positions");

            migrationBuilder.DropIndex(
                name: "ix_mail_folders_user_account_alias_generation",
                table: "mail_folders");

            migrationBuilder.DropIndex(
                name: "ix_mail_answering_audit_entries_run_user_account",
                table: "mail_answering_audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_mail_answering_audit_entries_user_account_completed",
                table: "mail_answering_audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_mail_account_assignments_mail_account_id",
                table: "mail_account_assignments");

            migrationBuilder.DropIndex(
                name: "ix_local_mail_folders_user_account_parent_name",
                table: "local_mail_folders");

            migrationBuilder.DropIndex(
                name: "ix_local_mail_folders_user_account_role",
                table: "local_mail_folders");

            migrationBuilder.DropIndex(
                name: "IX_local_mail_folders_UserId_MailboxAccountId_ErasedAt",
                table: "local_mail_folders");

            migrationBuilder.DropIndex(
                name: "ix_jobs_user_account",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "ix_jobs_user_turn",
                table: "jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_jobs_account_user",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "IX_email_threads_UserId_MailboxAccountId",
                table: "email_threads");

            migrationBuilder.DropPrimaryKey(
                name: "pk_email_thread_identifiers",
                table: "email_thread_identifiers");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "stored_content_claims");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "spam_classification_runs");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "outgoing_email_filings");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "mailbox_refresh_tokens");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "mailbox_mutations");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "mailbox_mutation_audit_entries");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "mailbox_accounts");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "mail_rule_executions");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "mail_rule_evaluation_runs");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "mail_rederivation_runs");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "mail_rederivation_positions");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "mail_folders");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "mail_answering_audit_entries");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "local_mail_folders");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "email_threads");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "email_thread_identifiers");

            migrationBuilder.AddColumn<string>(
                name: "MailboxAccountId",
                table: "stored_content_claims",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "outgoing_emails",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddPrimaryKey(
                name: "pk_spam_classification_runs",
                table: "spam_classification_runs",
                column: "MailboxAccountId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_mailbox_refresh_tokens",
                table: "mailbox_refresh_tokens",
                column: "MailboxAccountId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_mailbox_accounts",
                table: "mailbox_accounts",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mail_rule_evaluation_runs",
                table: "mail_rule_evaluation_runs",
                column: "MailboxAccountId");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mail_rederivation_runs",
                table: "mail_rederivation_runs",
                columns: new[] { "MailboxAccountId", "FolderAlias" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_mail_rederivation_positions",
                table: "mail_rederivation_positions",
                columns: new[] { "MailboxAccountId", "FolderAlias" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_email_thread_identifiers",
                table: "email_thread_identifiers",
                columns: new[] { "MailboxAccountId", "IdentifierHash" });

            migrationBuilder.CreateTable(
                name: "account_stored_content",
                columns: table => new
                {
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StoredContentByteCount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_stored_content", x => x.MailboxAccountId);
                    table.ForeignKey(
                        name: "FK_account_stored_content_mailbox_accounts_MailboxAccountId",
                        column: x => x.MailboxAccountId,
                        principalTable: "mailbox_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_account_identity",
                table: "stored_emails",
                columns: new[] { "MailboxAccountId", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_account_timeline",
                table: "stored_emails",
                columns: new[] { "MailboxAccountId", "ReceivedAt", "Id" },
                descending: new[] { false, true, true })
                .Annotation("Npgsql:IndexNullSortOrder", new[] { NullSortOrder.Unspecified, NullSortOrder.NullsLast, NullSortOrder.Unspecified });

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_awaiting_attachment_text",
                table: "stored_emails",
                columns: new[] { "MailboxAccountId", "Id" },
                filter: "\"AttachmentTextDerivedAt\" IS NULL AND \"AttachmentCount\" > 0");

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_awaiting_rule_evaluation",
                table: "stored_emails",
                columns: new[] { "MailboxAccountId", "Id" },
                filter: "\"RulesEvaluatedAt\" IS NULL AND \"FiledFromOutgoingEmailId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_filed_sent_copy",
                table: "stored_emails",
                columns: new[] { "MailboxAccountId", "InternetMessageId" },
                filter: "\"FiledFromOutgoingEmailId\" IS NOT NULL AND \"UidValidity\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_sends_identity",
                table: "recurring_sends",
                columns: new[] { "MailboxAccountId", "UserId", "RequesterOrigin", "RequesterIdentity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_emails_claimable",
                table: "outgoing_emails",
                columns: new[] { "MailboxAccountId", "AvailableAt", "Id" },
                filter: "\"Stage\" = 'Recorded'");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_emails_identity",
                table: "outgoing_emails",
                columns: new[] { "MailboxAccountId", "UserId", "RequesterOrigin", "RequesterIdentity" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_emails_outstanding",
                table: "outgoing_emails",
                columns: new[] { "MailboxAccountId", "RecordedAt" },
                filter: "\"Stage\" NOT IN ('Sent', 'Refused', 'Cancelled')");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_emails_period_usage",
                table: "outgoing_emails",
                columns: new[] { "RecordedAt", "MailboxAccountId" });

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_email_filings_message_id",
                table: "outgoing_email_filings",
                columns: new[] { "MailboxAccountId", "InternetMessageId" },
                filter: "\"Stage\" = 'Confirmed'");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_email_filings_placement",
                table: "outgoing_email_filings",
                columns: new[] { "MailboxAccountId", "FolderPath", "PlacementUidValidity", "PlacementUid" },
                filter: "\"Stage\" = 'Confirmed'");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_email_filings_recent_by_filing",
                table: "outgoing_email_filings",
                columns: new[] { "MailboxAccountId", "Filing", "AppendedAt", "OutgoingEmailId" },
                filter: "\"Stage\" = 'Confirmed'");

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_mutations_outstanding",
                table: "mailbox_mutations",
                columns: new[] { "MailboxAccountId", "RecordedAt" },
                filter: "\"Stage\" <> 'Completed'");

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_mutations_placement",
                table: "mailbox_mutations",
                columns: new[] { "MailboxAccountId", "DestinationFolderPath", "PlacementUidValidity", "PlacementUid" },
                filter: "\"PlacementObservedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_mutation_audit_entries_account_completed",
                table: "mailbox_mutation_audit_entries",
                columns: new[] { "MailboxAccountId", "CompletedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_rule_executions_account_evaluated",
                table: "mail_rule_executions",
                columns: new[] { "MailboxAccountId", "EvaluatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_rule_executions_account_rule_evaluated",
                table: "mail_rule_executions",
                columns: new[] { "MailboxAccountId", "RuleName", "EvaluatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_folders_account_alias_generation",
                table: "mail_folders",
                columns: new[] { "MailboxAccountId", "Alias", "ResolutionGeneration" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mail_answering_audit_entries_account_completed",
                table: "mail_answering_audit_entries",
                columns: new[] { "MailboxAccountId", "CompletedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_answering_audit_entries_run_account",
                table: "mail_answering_audit_entries",
                columns: new[] { "RunId", "MailboxAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mail_account_assignments_mail_account_id",
                table: "mail_account_assignments",
                column: "MailAccountId");

            migrationBuilder.CreateIndex(
                name: "ix_local_mail_folders_account_parent_name",
                table: "local_mail_folders",
                columns: new[] { "MailboxAccountId", "ParentId", "NameKey" },
                unique: true,
                filter: "\"ErasedAt\" IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_local_mail_folders_account_role",
                table: "local_mail_folders",
                columns: new[] { "MailboxAccountId", "Role" },
                unique: true,
                filter: "\"Role\" IS NOT NULL AND \"ErasedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_local_mail_folders_MailboxAccountId_ErasedAt",
                table: "local_mail_folders",
                columns: new[] { "MailboxAccountId", "ErasedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_account",
                table: "jobs",
                columns: new[] { "MailboxAccountId", "EnqueuedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_account_turn",
                table: "jobs",
                columns: new[] { "MailboxAccountId", "TurnAt" },
                filter: "\"State\" IN ('Pending', 'Claimed')");

            migrationBuilder.CreateIndex(
                name: "IX_email_threads_MailboxAccountId",
                table: "email_threads",
                column: "MailboxAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_email_threads_mailbox_accounts_MailboxAccountId",
                table: "email_threads",
                column: "MailboxAccountId",
                principalTable: "mailbox_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_jobs_mailbox_accounts_MailboxAccountId",
                table: "jobs",
                column: "MailboxAccountId",
                principalTable: "mailbox_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_local_mail_folders_mailbox_accounts_MailboxAccountId",
                table: "local_mail_folders",
                column: "MailboxAccountId",
                principalTable: "mailbox_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_mail_folders_mailbox_accounts_MailboxAccountId",
                table: "mail_folders",
                column: "MailboxAccountId",
                principalTable: "mailbox_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_email_threads_mailbox_accounts_MailboxAccountId",
                table: "email_threads");

            migrationBuilder.DropForeignKey(
                name: "FK_jobs_mailbox_accounts_MailboxAccountId",
                table: "jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_local_mail_folders_mailbox_accounts_MailboxAccountId",
                table: "local_mail_folders");

            migrationBuilder.DropForeignKey(
                name: "FK_mail_folders_mailbox_accounts_MailboxAccountId",
                table: "mail_folders");

            migrationBuilder.DropTable(
                name: "account_stored_content");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_account_identity",
                table: "stored_emails");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_account_timeline",
                table: "stored_emails");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_awaiting_attachment_text",
                table: "stored_emails");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_awaiting_rule_evaluation",
                table: "stored_emails");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_filed_sent_copy",
                table: "stored_emails");

            migrationBuilder.DropPrimaryKey(
                name: "pk_spam_classification_runs",
                table: "spam_classification_runs");

            migrationBuilder.DropIndex(
                name: "ix_recurring_sends_identity",
                table: "recurring_sends");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_emails_claimable",
                table: "outgoing_emails");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_emails_identity",
                table: "outgoing_emails");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_emails_outstanding",
                table: "outgoing_emails");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_emails_period_usage",
                table: "outgoing_emails");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_email_filings_message_id",
                table: "outgoing_email_filings");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_email_filings_placement",
                table: "outgoing_email_filings");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_email_filings_recent_by_filing",
                table: "outgoing_email_filings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_mailbox_refresh_tokens",
                table: "mailbox_refresh_tokens");

            migrationBuilder.DropIndex(
                name: "ix_mailbox_mutations_outstanding",
                table: "mailbox_mutations");

            migrationBuilder.DropIndex(
                name: "ix_mailbox_mutations_placement",
                table: "mailbox_mutations");

            migrationBuilder.DropIndex(
                name: "ix_mailbox_mutation_audit_entries_account_completed",
                table: "mailbox_mutation_audit_entries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_mailbox_accounts",
                table: "mailbox_accounts");

            migrationBuilder.DropIndex(
                name: "ix_mail_rule_executions_account_evaluated",
                table: "mail_rule_executions");

            migrationBuilder.DropIndex(
                name: "ix_mail_rule_executions_account_rule_evaluated",
                table: "mail_rule_executions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mail_rule_evaluation_runs",
                table: "mail_rule_evaluation_runs");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mail_rederivation_runs",
                table: "mail_rederivation_runs");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mail_rederivation_positions",
                table: "mail_rederivation_positions");

            migrationBuilder.DropIndex(
                name: "ix_mail_folders_account_alias_generation",
                table: "mail_folders");

            migrationBuilder.DropIndex(
                name: "ix_mail_answering_audit_entries_account_completed",
                table: "mail_answering_audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_mail_answering_audit_entries_run_account",
                table: "mail_answering_audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_mail_account_assignments_mail_account_id",
                table: "mail_account_assignments");

            migrationBuilder.DropIndex(
                name: "ix_local_mail_folders_account_parent_name",
                table: "local_mail_folders");

            migrationBuilder.DropIndex(
                name: "ix_local_mail_folders_account_role",
                table: "local_mail_folders");

            migrationBuilder.DropIndex(
                name: "IX_local_mail_folders_MailboxAccountId_ErasedAt",
                table: "local_mail_folders");

            migrationBuilder.DropIndex(
                name: "ix_jobs_account",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "ix_jobs_account_turn",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "IX_email_threads_MailboxAccountId",
                table: "email_threads");

            migrationBuilder.DropPrimaryKey(
                name: "pk_email_thread_identifiers",
                table: "email_thread_identifiers");

            migrationBuilder.DropColumn(
                name: "MailboxAccountId",
                table: "stored_content_claims");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "stored_emails",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "stored_content_claims",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "spam_classification_runs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "outgoing_emails",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "outgoing_email_filings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "mailbox_refresh_tokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "mailbox_mutations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "mailbox_mutation_audit_entries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "mailbox_accounts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "mail_rule_executions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "mail_rule_evaluation_runs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "mail_rederivation_runs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "mail_rederivation_positions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "mail_folders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "mail_answering_audit_entries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "local_mail_folders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "email_threads",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "email_thread_identifiers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "pk_spam_classification_runs",
                table: "spam_classification_runs",
                columns: new[] { "UserId", "MailboxAccountId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_mailbox_refresh_tokens",
                table: "mailbox_refresh_tokens",
                columns: new[] { "UserId", "MailboxAccountId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_mailbox_accounts",
                table: "mailbox_accounts",
                columns: new[] { "UserId", "Id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_mail_rule_evaluation_runs",
                table: "mail_rule_evaluation_runs",
                columns: new[] { "UserId", "MailboxAccountId" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_mail_rederivation_runs",
                table: "mail_rederivation_runs",
                columns: new[] { "UserId", "MailboxAccountId", "FolderAlias" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_mail_rederivation_positions",
                table: "mail_rederivation_positions",
                columns: new[] { "UserId", "MailboxAccountId", "FolderAlias" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_email_thread_identifiers",
                table: "email_thread_identifiers",
                columns: new[] { "UserId", "MailboxAccountId", "IdentifierHash" });

            migrationBuilder.CreateTable(
                name: "user_stored_content",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoredContentByteCount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_stored_content", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_user_stored_content_settings_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "settings_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_awaiting_attachment_text",
                table: "stored_emails",
                columns: new[] { "UserId", "MailboxAccountId", "Id" },
                filter: "\"AttachmentTextDerivedAt\" IS NULL AND \"AttachmentCount\" > 0");

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_awaiting_rule_evaluation",
                table: "stored_emails",
                columns: new[] { "UserId", "MailboxAccountId", "Id" },
                filter: "\"RulesEvaluatedAt\" IS NULL AND \"FiledFromOutgoingEmailId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_filed_sent_copy",
                table: "stored_emails",
                columns: new[] { "UserId", "MailboxAccountId", "InternetMessageId" },
                filter: "\"FiledFromOutgoingEmailId\" IS NOT NULL AND \"UidValidity\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_user_account_identity",
                table: "stored_emails",
                columns: new[] { "UserId", "MailboxAccountId", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_user_account_timeline",
                table: "stored_emails",
                columns: new[] { "UserId", "MailboxAccountId", "ReceivedAt", "Id" },
                descending: new[] { false, false, true, true })
                .Annotation("Npgsql:IndexNullSortOrder", new[] { NullSortOrder.Unspecified, NullSortOrder.Unspecified, NullSortOrder.NullsLast, NullSortOrder.Unspecified });

            migrationBuilder.CreateIndex(
                name: "ix_recurring_sends_identity",
                table: "recurring_sends",
                columns: new[] { "UserId", "MailboxAccountId", "RequesterOrigin", "RequesterIdentity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_emails_claimable",
                table: "outgoing_emails",
                columns: new[] { "UserId", "MailboxAccountId", "AvailableAt", "Id" },
                filter: "\"Stage\" = 'Recorded'");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_emails_identity",
                table: "outgoing_emails",
                columns: new[] { "UserId", "MailboxAccountId", "RequesterOrigin", "RequesterIdentity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_emails_outstanding",
                table: "outgoing_emails",
                columns: new[] { "UserId", "MailboxAccountId", "RecordedAt" },
                filter: "\"Stage\" NOT IN ('Sent', 'Refused', 'Cancelled')");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_emails_period_usage",
                table: "outgoing_emails",
                columns: new[] { "RecordedAt", "UserId", "MailboxAccountId" });

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_email_filings_message_id",
                table: "outgoing_email_filings",
                columns: new[] { "UserId", "MailboxAccountId", "InternetMessageId" },
                filter: "\"Stage\" = 'Confirmed'");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_email_filings_placement",
                table: "outgoing_email_filings",
                columns: new[] { "UserId", "MailboxAccountId", "FolderPath", "PlacementUidValidity", "PlacementUid" },
                filter: "\"Stage\" = 'Confirmed'");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_email_filings_recent_by_filing",
                table: "outgoing_email_filings",
                columns: new[] { "UserId", "MailboxAccountId", "Filing", "AppendedAt", "OutgoingEmailId" },
                filter: "\"Stage\" = 'Confirmed'");

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_mutations_outstanding",
                table: "mailbox_mutations",
                columns: new[] { "UserId", "MailboxAccountId", "RecordedAt" },
                filter: "\"Stage\" <> 'Completed'");

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_mutations_placement",
                table: "mailbox_mutations",
                columns: new[] { "UserId", "MailboxAccountId", "DestinationFolderPath", "PlacementUidValidity", "PlacementUid" },
                filter: "\"PlacementObservedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_mutation_audit_entries_user_account_completed",
                table: "mailbox_mutation_audit_entries",
                columns: new[] { "UserId", "MailboxAccountId", "CompletedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_rule_executions_user_account_evaluated",
                table: "mail_rule_executions",
                columns: new[] { "UserId", "MailboxAccountId", "EvaluatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_rule_executions_user_account_rule_evaluated",
                table: "mail_rule_executions",
                columns: new[] { "UserId", "MailboxAccountId", "RuleName", "EvaluatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_folders_user_account_alias_generation",
                table: "mail_folders",
                columns: new[] { "UserId", "MailboxAccountId", "Alias", "ResolutionGeneration" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mail_answering_audit_entries_run_user_account",
                table: "mail_answering_audit_entries",
                columns: new[] { "RunId", "UserId", "MailboxAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mail_answering_audit_entries_user_account_completed",
                table: "mail_answering_audit_entries",
                columns: new[] { "UserId", "MailboxAccountId", "CompletedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_account_assignments_mail_account_id",
                table: "mail_account_assignments",
                column: "MailAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_mail_folders_user_account_parent_name",
                table: "local_mail_folders",
                columns: new[] { "UserId", "MailboxAccountId", "ParentId", "NameKey" },
                unique: true,
                filter: "\"ErasedAt\" IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_local_mail_folders_user_account_role",
                table: "local_mail_folders",
                columns: new[] { "UserId", "MailboxAccountId", "Role" },
                unique: true,
                filter: "\"Role\" IS NOT NULL AND \"ErasedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_local_mail_folders_UserId_MailboxAccountId_ErasedAt",
                table: "local_mail_folders",
                columns: new[] { "UserId", "MailboxAccountId", "ErasedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_user_account",
                table: "jobs",
                columns: new[] { "UserId", "MailboxAccountId", "EnqueuedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_user_turn",
                table: "jobs",
                columns: new[] { "UserId", "TurnAt" },
                filter: "\"State\" IN ('Pending', 'Claimed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_account_user",
                table: "jobs",
                sql: "(\"UserId\" IS NULL) = (\"MailboxAccountId\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_email_threads_UserId_MailboxAccountId",
                table: "email_threads",
                columns: new[] { "UserId", "MailboxAccountId" });

            migrationBuilder.AddForeignKey(
                name: "FK_email_threads_mailbox_accounts_UserId_MailboxAccountId",
                table: "email_threads",
                columns: new[] { "UserId", "MailboxAccountId" },
                principalTable: "mailbox_accounts",
                principalColumns: new[] { "UserId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_jobs_mailbox_accounts_UserId_MailboxAccountId",
                table: "jobs",
                columns: new[] { "UserId", "MailboxAccountId" },
                principalTable: "mailbox_accounts",
                principalColumns: new[] { "UserId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_local_mail_folders_mailbox_accounts_UserId_MailboxAccountId",
                table: "local_mail_folders",
                columns: new[] { "UserId", "MailboxAccountId" },
                principalTable: "mailbox_accounts",
                principalColumns: new[] { "UserId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_mail_folders_mailbox_accounts_UserId_MailboxAccountId",
                table: "mail_folders",
                columns: new[] { "UserId", "MailboxAccountId" },
                principalTable: "mailbox_accounts",
                principalColumns: new[] { "UserId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_mailbox_accounts_settings_accounts_UserId",
                table: "mailbox_accounts",
                column: "UserId",
                principalTable: "settings_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

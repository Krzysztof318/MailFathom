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
    /// <remarks>
    /// <para>
    /// Every table that references a mail account gains the owner that account belongs to, and every index that led
    /// with the account identifier is replaced by one leading with the pair. An identifier is the name an operator
    /// wrote and names one account within its owner, so a structure led by the identifier alone would interleave two
    /// owners' rows under one key once a deployment serves more than one person.
    /// </para>
    /// <para>
    /// The generated shape would have added each column as required with the zero identifier as its default, which
    /// silently attributes every row a database already holds to an owner that does not exist. The statements between
    /// the generated operations are what make it apply forward instead: the columns arrive nullable, every row is
    /// carried onto the owner of the account it names, whatever is left is carried onto the one owner the deployment
    /// was serving, and only then does the column become required. <c>jobs</c> keeps a nullable owner, because its
    /// account reference is nullable and a queue row about no account belongs to nobody.
    /// </para>
    /// <para>
    /// <c>mailbox_accounts</c> keeps its single-column key, so no identifier that was legal before this migration is
    /// refused after it. Reverting is free of data loss in a way the forward direction is not: <c>Down</c> drops the
    /// columns and restores every index the account alone led, which is a schema a single-owner deployment is still
    /// correct under.
    /// </para>
    /// </remarks>
    public partial class CarryOwnerBesideMailAccountReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stored_emails_account_identity",
                table: "stored_emails");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_account_timeline",
                table: "stored_emails");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_awaiting_rule_evaluation",
                table: "stored_emails");

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
                name: "ix_mailbox_mutations_outstanding",
                table: "mailbox_mutations");

            migrationBuilder.DropIndex(
                name: "ix_mailbox_mutations_placement",
                table: "mailbox_mutations");

            migrationBuilder.DropIndex(
                name: "ix_mailbox_mutation_audit_entries_account_completed",
                table: "mailbox_mutation_audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_mail_rule_executions_account_evaluated",
                table: "mail_rule_executions");

            migrationBuilder.DropIndex(
                name: "ix_mail_rule_executions_account_rule_evaluated",
                table: "mail_rule_executions");

            migrationBuilder.DropIndex(
                name: "ix_mail_folders_account_alias_generation",
                table: "mail_folders");

            migrationBuilder.DropIndex(
                name: "ix_mail_drafts_account_revised",
                table: "mail_drafts");

            migrationBuilder.DropIndex(
                name: "ix_mail_answering_audit_entries_account_completed",
                table: "mail_answering_audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_mail_answering_audit_entries_run_account",
                table: "mail_answering_audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_jobs_account",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "ix_jobs_account_turn",
                table: "jobs");

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "stored_emails",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "spam_classification_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "recurring_sends",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "outgoing_emails",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "outgoing_email_filings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "mailbox_refresh_tokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "mailbox_mutations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "mailbox_mutation_audit_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "mail_rule_executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "mail_rule_evaluation_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "mail_rederivation_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "mail_rederivation_positions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "mail_folders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "mail_drafts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "mail_answering_audit_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "email_threads",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "email_thread_identifiers",
                type: "uuid",
                nullable: true);

            // Every row that names an account takes that account's owner, which is the column AddOwnerAccounts put on
            // mailbox_accounts and #981 filled. Two tables are carried from the row they hang off instead, because
            // that association is a foreign key while the account identifier beside it is a copy: a stored email
            // through its folder binding, and a thread identifier through its thread.
            FillOwnerFromAccount(migrationBuilder, "mail_folders");
            FillOwnerFromAccount(migrationBuilder, "email_threads");
            FillOwnerFromAccount(migrationBuilder, "mail_drafts");
            FillOwnerFromAccount(migrationBuilder, "mail_answering_audit_entries");
            FillOwnerFromAccount(migrationBuilder, "mail_rederivation_positions");
            FillOwnerFromAccount(migrationBuilder, "mail_rederivation_runs");
            FillOwnerFromAccount(migrationBuilder, "mail_rule_evaluation_runs");
            FillOwnerFromAccount(migrationBuilder, "mail_rule_executions");
            FillOwnerFromAccount(migrationBuilder, "mailbox_mutation_audit_entries");
            FillOwnerFromAccount(migrationBuilder, "mailbox_mutations");
            FillOwnerFromAccount(migrationBuilder, "mailbox_refresh_tokens");
            FillOwnerFromAccount(migrationBuilder, "outgoing_email_filings");
            FillOwnerFromAccount(migrationBuilder, "outgoing_emails");
            FillOwnerFromAccount(migrationBuilder, "recurring_sends");
            FillOwnerFromAccount(migrationBuilder, "spam_classification_runs");
            FillOwnerFromAccount(migrationBuilder, "jobs");

            migrationBuilder.Sql(
                """
                UPDATE stored_emails
                SET "OwnerId" = mail_folders."OwnerId"
                FROM mail_folders
                WHERE mail_folders."Id" = stored_emails."MailFolderId"
                  AND stored_emails."OwnerId" IS NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE email_thread_identifiers
                SET "OwnerId" = email_threads."OwnerId"
                FROM email_threads
                WHERE email_threads."Id" = email_thread_identifiers."EmailThreadId"
                  AND email_thread_identifiers."OwnerId" IS NULL;
                """);

            // What is left names an account this deployment no longer holds a row for — a mailbox removed from the
            // configuration keeps the history it produced, and only three of these tables key into mailbox_accounts at
            // all. Such a row belongs to the one owner the deployment was serving when it was written, which is the
            // invariant IDeploymentMailOwnerSource states and which #1248 is what ends. The subquery names the row
            // AddOwnerAccounts inserted rather than a value repeated here.
            FillOwnerFromDeployment(migrationBuilder, "mail_drafts");
            FillOwnerFromDeployment(migrationBuilder, "mail_answering_audit_entries");
            FillOwnerFromDeployment(migrationBuilder, "mail_rederivation_positions");
            FillOwnerFromDeployment(migrationBuilder, "mail_rederivation_runs");
            FillOwnerFromDeployment(migrationBuilder, "mail_rule_evaluation_runs");
            FillOwnerFromDeployment(migrationBuilder, "mail_rule_executions");
            FillOwnerFromDeployment(migrationBuilder, "mailbox_mutation_audit_entries");
            FillOwnerFromDeployment(migrationBuilder, "mailbox_mutations");
            FillOwnerFromDeployment(migrationBuilder, "mailbox_refresh_tokens");
            FillOwnerFromDeployment(migrationBuilder, "outgoing_email_filings");
            FillOwnerFromDeployment(migrationBuilder, "outgoing_emails");
            FillOwnerFromDeployment(migrationBuilder, "recurring_sends");
            FillOwnerFromDeployment(migrationBuilder, "spam_classification_runs");
            FillOwnerFromDeployment(migrationBuilder, "email_thread_identifiers");
            FillOwnerFromDeployment(migrationBuilder, "stored_emails");
            FillOwnerFromDeployment(migrationBuilder, "email_threads");
            FillOwnerFromDeployment(migrationBuilder, "mail_folders");

            // Required only now, and on every table but jobs: a queue row may belong to no account at all, so its
            // owner is absent for exactly the rows its account is.
            RequireOwner(migrationBuilder, "email_thread_identifiers");
            RequireOwner(migrationBuilder, "email_threads");
            RequireOwner(migrationBuilder, "mail_answering_audit_entries");
            RequireOwner(migrationBuilder, "mail_drafts");
            RequireOwner(migrationBuilder, "mail_folders");
            RequireOwner(migrationBuilder, "mail_rederivation_positions");
            RequireOwner(migrationBuilder, "mail_rederivation_runs");
            RequireOwner(migrationBuilder, "mail_rule_evaluation_runs");
            RequireOwner(migrationBuilder, "mail_rule_executions");
            RequireOwner(migrationBuilder, "mailbox_mutation_audit_entries");
            RequireOwner(migrationBuilder, "mailbox_mutations");
            RequireOwner(migrationBuilder, "mailbox_refresh_tokens");
            RequireOwner(migrationBuilder, "outgoing_email_filings");
            RequireOwner(migrationBuilder, "outgoing_emails");
            RequireOwner(migrationBuilder, "recurring_sends");
            RequireOwner(migrationBuilder, "spam_classification_runs");
            RequireOwner(migrationBuilder, "stored_emails");

            // The queued documents are carried across too, and this is the one place they can be. Every payload that
            // names an account now names its owner as well and declares the property required, so a document written by
            // the previous release no longer parses — and the claim reads a batch of rows rather than one, so a single
            // unparseable document would take every job claimed beside it with it, on a queue that would then never
            // drain. The owner is on the row by the statements above, so the value is already here; a payload that
            // names no account has no owner to write and is left alone.
            migrationBuilder.Sql(
                """
                UPDATE jobs
                SET "Payload" = jsonb_set("Payload", '{ownerId}', to_jsonb("OwnerId"))
                WHERE "OwnerId" IS NOT NULL
                  AND NOT jsonb_exists("Payload", 'ownerId');
                """);

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_awaiting_rule_evaluation",
                table: "stored_emails",
                columns: new[] { "OwnerId", "MailboxAccountId", "Id" },
                filter: "\"RulesEvaluatedAt\" IS NULL AND \"FiledFromOutgoingEmailId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_owner_account_identity",
                table: "stored_emails",
                columns: new[] { "OwnerId", "MailboxAccountId", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_owner_account_timeline",
                table: "stored_emails",
                columns: new[] { "OwnerId", "MailboxAccountId", "ReceivedAt", "Id" },
                descending: new[] { false, false, true, true })
                .Annotation("Npgsql:IndexNullSortOrder", new[] { NullSortOrder.Unspecified, NullSortOrder.Unspecified, NullSortOrder.NullsLast, NullSortOrder.Unspecified });

            migrationBuilder.CreateIndex(
                name: "ix_recurring_sends_identity",
                table: "recurring_sends",
                columns: new[] { "OwnerId", "MailboxAccountId", "RequesterOrigin", "RequesterIdentity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_emails_claimable",
                table: "outgoing_emails",
                columns: new[] { "OwnerId", "MailboxAccountId", "AvailableAt", "Id" },
                filter: "\"Stage\" = 'Recorded'");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_emails_identity",
                table: "outgoing_emails",
                columns: new[] { "OwnerId", "MailboxAccountId", "RequesterOrigin", "RequesterIdentity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_emails_outstanding",
                table: "outgoing_emails",
                columns: new[] { "OwnerId", "MailboxAccountId", "RecordedAt" },
                filter: "\"Stage\" NOT IN ('Sent', 'Refused', 'Cancelled')");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_emails_period_usage",
                table: "outgoing_emails",
                columns: new[] { "RecordedAt", "OwnerId", "MailboxAccountId" });

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_email_filings_message_id",
                table: "outgoing_email_filings",
                columns: new[] { "OwnerId", "MailboxAccountId", "InternetMessageId" },
                filter: "\"ObservedAt\" IS NULL AND \"Stage\" = 'Confirmed'");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_email_filings_placement",
                table: "outgoing_email_filings",
                columns: new[] { "OwnerId", "MailboxAccountId", "FolderPath", "PlacementUidValidity", "PlacementUid" },
                filter: "\"ObservedAt\" IS NULL AND \"Stage\" = 'Confirmed'");

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_mutations_outstanding",
                table: "mailbox_mutations",
                columns: new[] { "OwnerId", "MailboxAccountId", "RecordedAt" },
                filter: "\"Stage\" <> 'Completed'");

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_mutations_placement",
                table: "mailbox_mutations",
                columns: new[] { "OwnerId", "MailboxAccountId", "DestinationFolderPath", "PlacementUidValidity", "PlacementUid" },
                filter: "\"PlacementObservedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_mutation_audit_entries_owner_account_completed",
                table: "mailbox_mutation_audit_entries",
                columns: new[] { "OwnerId", "MailboxAccountId", "CompletedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_rule_executions_owner_account_evaluated",
                table: "mail_rule_executions",
                columns: new[] { "OwnerId", "MailboxAccountId", "EvaluatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_rule_executions_owner_account_rule_evaluated",
                table: "mail_rule_executions",
                columns: new[] { "OwnerId", "MailboxAccountId", "RuleName", "EvaluatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_mail_folders_MailboxAccountId",
                table: "mail_folders",
                column: "MailboxAccountId");

            migrationBuilder.CreateIndex(
                name: "ix_mail_folders_owner_account_alias_generation",
                table: "mail_folders",
                columns: new[] { "OwnerId", "MailboxAccountId", "Alias", "ResolutionGeneration" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mail_drafts_owner_account_revised",
                table: "mail_drafts",
                columns: new[] { "OwnerId", "MailboxAccountId", "RevisedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_answering_audit_entries_owner_account_completed",
                table: "mail_answering_audit_entries",
                columns: new[] { "OwnerId", "MailboxAccountId", "CompletedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_answering_audit_entries_run_owner_account",
                table: "mail_answering_audit_entries",
                columns: new[] { "RunId", "OwnerId", "MailboxAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_jobs_MailboxAccountId",
                table: "jobs",
                column: "MailboxAccountId");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_owner_account",
                table: "jobs",
                columns: new[] { "OwnerId", "MailboxAccountId", "EnqueuedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_owner_turn",
                table: "jobs",
                columns: new[] { "OwnerId", "TurnAt" },
                filter: "\"State\" IN ('Pending', 'Claimed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The payload rewrite in Up has no mirror here, deliberately. The contract it wrote into is source-generated
            // with the default unmapped-member handling, so a release without the owner reads a document carrying one
            // and ignores the property; taking it back out would rewrite every queued document to no effect, and a
            // second Up would then have to write it again.
            migrationBuilder.DropIndex(
                name: "ix_stored_emails_awaiting_rule_evaluation",
                table: "stored_emails");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_owner_account_identity",
                table: "stored_emails");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_owner_account_timeline",
                table: "stored_emails");

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
                name: "ix_mailbox_mutations_outstanding",
                table: "mailbox_mutations");

            migrationBuilder.DropIndex(
                name: "ix_mailbox_mutations_placement",
                table: "mailbox_mutations");

            migrationBuilder.DropIndex(
                name: "ix_mailbox_mutation_audit_entries_owner_account_completed",
                table: "mailbox_mutation_audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_mail_rule_executions_owner_account_evaluated",
                table: "mail_rule_executions");

            migrationBuilder.DropIndex(
                name: "ix_mail_rule_executions_owner_account_rule_evaluated",
                table: "mail_rule_executions");

            migrationBuilder.DropIndex(
                name: "IX_mail_folders_MailboxAccountId",
                table: "mail_folders");

            migrationBuilder.DropIndex(
                name: "ix_mail_folders_owner_account_alias_generation",
                table: "mail_folders");

            migrationBuilder.DropIndex(
                name: "ix_mail_drafts_owner_account_revised",
                table: "mail_drafts");

            migrationBuilder.DropIndex(
                name: "ix_mail_answering_audit_entries_owner_account_completed",
                table: "mail_answering_audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_mail_answering_audit_entries_run_owner_account",
                table: "mail_answering_audit_entries");

            migrationBuilder.DropIndex(
                name: "IX_jobs_MailboxAccountId",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "ix_jobs_owner_account",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "ix_jobs_owner_turn",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "spam_classification_runs");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "recurring_sends");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "outgoing_emails");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "outgoing_email_filings");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "mailbox_refresh_tokens");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "mailbox_mutations");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "mailbox_mutation_audit_entries");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "mail_rule_executions");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "mail_rule_evaluation_runs");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "mail_rederivation_runs");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "mail_rederivation_positions");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "mail_folders");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "mail_drafts");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "mail_answering_audit_entries");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "email_threads");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "email_thread_identifiers");

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
                name: "ix_stored_emails_awaiting_rule_evaluation",
                table: "stored_emails",
                columns: new[] { "MailboxAccountId", "Id" },
                filter: "\"RulesEvaluatedAt\" IS NULL AND \"FiledFromOutgoingEmailId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_sends_identity",
                table: "recurring_sends",
                columns: new[] { "MailboxAccountId", "RequesterOrigin", "RequesterIdentity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_emails_claimable",
                table: "outgoing_emails",
                columns: new[] { "MailboxAccountId", "AvailableAt", "Id" },
                filter: "\"Stage\" = 'Recorded'");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_emails_identity",
                table: "outgoing_emails",
                columns: new[] { "MailboxAccountId", "RequesterOrigin", "RequesterIdentity" },
                unique: true);

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
                filter: "\"ObservedAt\" IS NULL AND \"Stage\" = 'Confirmed'");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_email_filings_placement",
                table: "outgoing_email_filings",
                columns: new[] { "MailboxAccountId", "FolderPath", "PlacementUidValidity", "PlacementUid" },
                filter: "\"ObservedAt\" IS NULL AND \"Stage\" = 'Confirmed'");

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
                name: "ix_mail_drafts_account_revised",
                table: "mail_drafts",
                columns: new[] { "MailboxAccountId", "RevisedAt" });

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
                name: "ix_jobs_account",
                table: "jobs",
                columns: new[] { "MailboxAccountId", "EnqueuedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_account_turn",
                table: "jobs",
                columns: new[] { "MailboxAccountId", "TurnAt" },
                filter: "\"State\" IN ('Pending', 'Claimed')");
        }

        /// <summary>Carries every row of one table onto the owner of the account it names.</summary>
        /// <remarks>
        /// One statement per table rather than one over a union, because each is a separate scan the database can
        /// report on its own and because the tables are of wildly different sizes. Rows already filled are skipped, so
        /// re-running a half-applied statement costs a scan rather than overwriting an answer.
        /// </remarks>
        private static void FillOwnerFromAccount(MigrationBuilder migrationBuilder, string table) =>
            migrationBuilder.Sql(
                $"""
                 UPDATE {table}
                 SET "OwnerId" = mailbox_accounts."OwnerId"
                 FROM mailbox_accounts
                 WHERE mailbox_accounts."Id" = {table}."MailboxAccountId"
                   AND {table}."OwnerId" IS NULL;
                 """);

        /// <summary>Carries whatever is left onto the one owner this deployment was serving.</summary>
        private static void FillOwnerFromDeployment(MigrationBuilder migrationBuilder, string table) =>
            migrationBuilder.Sql(
                $"""
                 UPDATE {table}
                 SET "OwnerId" = (SELECT "Id" FROM settings_accounts ORDER BY "CreatedAt", "Id" LIMIT 1)
                 WHERE "OwnerId" IS NULL;
                 """);

        /// <summary>Makes the filled column required, which is what the reads and the indexes above depend on.</summary>
        private static void RequireOwner(MigrationBuilder migrationBuilder, string table) =>
            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerId",
                table: table,
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
    }
}

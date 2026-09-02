// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// A mail account is keyed by its owner and the identifier its operator chose, so the identifier names one mailbox
    /// within its owner and nothing across the deployment. Every foreign key onto the account becomes the pair, and the
    /// six keys that were led by the account identifier gain the owner in front of it. Nothing is renamed, no column is
    /// added or dropped, and no value is rewritten: the columns this reorders were put there and filled by
    /// <c>CarryOwnerBesideMailAccountReferences</c>, so this migration is a key change over data that already agrees
    /// with it.
    /// </para>
    /// <para>
    /// It applies forward without a fill for the same reason. Every row on the three tables keying onto the account
    /// took its owner from the account it names, so each pair already resolves and the foreign keys validate rather
    /// than fail; the owner on a queue row is present for exactly the rows an account is, which is what the new check
    /// states rather than establishes.
    /// </para>
    /// <para>
    /// What an operator should expect is an index rebuild and a brief exclusive lock per table rather than a table
    /// rewrite, over ten tables rather than the seven whose keys move. <c>ALTER TABLE ... ADD PRIMARY KEY</c> builds
    /// the new index and changes no row, and of those seven only <c>email_thread_identifiers</c> is proportional to
    /// the mail corpus — the other six hold roughly one row per account, or per account and folder. The other three
    /// are <c>email_threads</c>, <c>jobs</c>, and <c>mail_folders</c>, whose foreign key onto the account is dropped
    /// and re-added as the pair: a foreign key and a check are each validated by a full scan of the table they are
    /// added to, so each of the three is scanned once and <c>jobs</c> twice for the check beside it. A second of those
    /// grows with the mail corpus — <c>email_threads</c> holds one row per conversation — so a maintenance window is
    /// sized from two corpus-proportional tables rather than one. The whole of it runs in one transaction, so a
    /// deployment that cannot take the lock retries the migration rather than resuming a half-applied one.
    /// </para>
    /// <para>
    /// <c>Down</c> restores the single-column keys and the indexes that led with the identifier alone, which is a
    /// schema a deployment serving one owner is still correct under. It refuses to apply where two owners have since
    /// declared an account under one identifier, and that refusal is the point: reverting cannot decide which of them
    /// keeps the name.
    /// </para>
    /// </remarks>
    public partial class KeyMailAccountByOwnerAndIdentifier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_email_threads_mailbox_accounts_MailboxAccountId",
                table: "email_threads");

            migrationBuilder.DropForeignKey(
                name: "FK_jobs_mailbox_accounts_MailboxAccountId",
                table: "jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_mail_folders_mailbox_accounts_MailboxAccountId",
                table: "mail_folders");

            migrationBuilder.DropPrimaryKey(
                name: "pk_spam_classification_runs",
                table: "spam_classification_runs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_mailbox_refresh_tokens",
                table: "mailbox_refresh_tokens");

            migrationBuilder.DropPrimaryKey(
                name: "PK_mailbox_accounts",
                table: "mailbox_accounts");

            migrationBuilder.DropIndex(
                name: "IX_mailbox_accounts_OwnerId",
                table: "mailbox_accounts");

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
                name: "IX_mail_folders_MailboxAccountId",
                table: "mail_folders");

            migrationBuilder.DropIndex(
                name: "IX_jobs_MailboxAccountId",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "IX_email_threads_MailboxAccountId",
                table: "email_threads");

            migrationBuilder.DropPrimaryKey(
                name: "pk_email_thread_identifiers",
                table: "email_thread_identifiers");

            migrationBuilder.AddPrimaryKey(
                name: "pk_spam_classification_runs",
                table: "spam_classification_runs",
                columns: new[] { "OwnerId", "MailboxAccountId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_mailbox_refresh_tokens",
                table: "mailbox_refresh_tokens",
                columns: new[] { "OwnerId", "MailboxAccountId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_mailbox_accounts",
                table: "mailbox_accounts",
                columns: new[] { "OwnerId", "Id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_mail_rule_evaluation_runs",
                table: "mail_rule_evaluation_runs",
                columns: new[] { "OwnerId", "MailboxAccountId" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_mail_rederivation_runs",
                table: "mail_rederivation_runs",
                columns: new[] { "OwnerId", "MailboxAccountId", "FolderAlias" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_mail_rederivation_positions",
                table: "mail_rederivation_positions",
                columns: new[] { "OwnerId", "MailboxAccountId", "FolderAlias" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_email_thread_identifiers",
                table: "email_thread_identifiers",
                columns: new[] { "OwnerId", "MailboxAccountId", "IdentifierHash" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_account_owner",
                table: "jobs",
                sql: "(\"OwnerId\" IS NULL) = (\"MailboxAccountId\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_email_threads_OwnerId_MailboxAccountId",
                table: "email_threads",
                columns: new[] { "OwnerId", "MailboxAccountId" });

            migrationBuilder.AddForeignKey(
                name: "FK_email_threads_mailbox_accounts_OwnerId_MailboxAccountId",
                table: "email_threads",
                columns: new[] { "OwnerId", "MailboxAccountId" },
                principalTable: "mailbox_accounts",
                principalColumns: new[] { "OwnerId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_jobs_mailbox_accounts_OwnerId_MailboxAccountId",
                table: "jobs",
                columns: new[] { "OwnerId", "MailboxAccountId" },
                principalTable: "mailbox_accounts",
                principalColumns: new[] { "OwnerId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_mail_folders_mailbox_accounts_OwnerId_MailboxAccountId",
                table: "mail_folders",
                columns: new[] { "OwnerId", "MailboxAccountId" },
                principalTable: "mailbox_accounts",
                principalColumns: new[] { "OwnerId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_email_threads_mailbox_accounts_OwnerId_MailboxAccountId",
                table: "email_threads");

            migrationBuilder.DropForeignKey(
                name: "FK_jobs_mailbox_accounts_OwnerId_MailboxAccountId",
                table: "jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_mail_folders_mailbox_accounts_OwnerId_MailboxAccountId",
                table: "mail_folders");

            migrationBuilder.DropPrimaryKey(
                name: "pk_spam_classification_runs",
                table: "spam_classification_runs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_mailbox_refresh_tokens",
                table: "mailbox_refresh_tokens");

            migrationBuilder.DropPrimaryKey(
                name: "PK_mailbox_accounts",
                table: "mailbox_accounts");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mail_rule_evaluation_runs",
                table: "mail_rule_evaluation_runs");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mail_rederivation_runs",
                table: "mail_rederivation_runs");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mail_rederivation_positions",
                table: "mail_rederivation_positions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_jobs_account_owner",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "IX_email_threads_OwnerId_MailboxAccountId",
                table: "email_threads");

            migrationBuilder.DropPrimaryKey(
                name: "pk_email_thread_identifiers",
                table: "email_thread_identifiers");

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

            migrationBuilder.CreateIndex(
                name: "IX_mailbox_accounts_OwnerId",
                table: "mailbox_accounts",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_mail_folders_MailboxAccountId",
                table: "mail_folders",
                column: "MailboxAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_jobs_MailboxAccountId",
                table: "jobs",
                column: "MailboxAccountId");

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
                name: "FK_mail_folders_mailbox_accounts_MailboxAccountId",
                table: "mail_folders",
                column: "MailboxAccountId",
                principalTable: "mailbox_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeStoredEmailOccurrenceOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "UidValidity",
                table: "stored_emails",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "Uid",
                table: "stored_emails",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stored_emails_occurrence_complete",
                table: "stored_emails",
                sql: "(\"UidValidity\" IS NULL) = (\"Uid\" IS NULL)");

            // A queued classification named the message by its occurrence and now names it by its stored identity, and
            // the payload declares that property required, so a document written by the previous release no longer
            // parses — and one unparseable document fails every job claimed in the same batch. Each is rewritten from
            // the row its occurrence resolves to. What resolves to nothing named a message that has already left, which
            // the handler answered by ending the job without acting, so those are removed rather than left to fail.
            migrationBuilder.Sql(
                """
                UPDATE jobs AS job
                SET "Payload" = jsonb_build_object(
                    'userId', job."Payload" -> 'userId',
                    'accountId', job."Payload" -> 'accountId',
                    'storedEmailId', to_jsonb(email."Id"))
                FROM stored_emails AS email
                JOIN mail_folders AS folder ON folder."Id" = email."MailFolderId"
                WHERE job."JobType" = 'classify-email-spam'
                  AND jsonb_exists(job."Payload", 'uid')
                  AND folder."UserId" = (job."Payload" ->> 'userId')::uuid
                  AND folder."MailboxAccountId" = job."Payload" ->> 'accountId'
                  AND folder."Alias" = job."Payload" ->> 'folderAlias'
                  AND folder."ResolutionGeneration" = (job."Payload" ->> 'folderResolutionGeneration')::integer
                  AND email."UidValidity" = (job."Payload" ->> 'uidValidity')::bigint
                  AND email."Uid" = (job."Payload" ->> 'uid')::bigint;

                DELETE FROM jobs
                WHERE "JobType" = 'classify-email-spam'
                  AND jsonb_exists("Payload", 'uid');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restoring NOT NULL fails on a row that carries no occurrence, deliberately: such a row is a message no
            // server holds, and no occurrence could be written back for it. The payload rewrite in Up has no mirror
            // either, because the contract it wrote into is the one this release reads.
            migrationBuilder.DropCheckConstraint(
                name: "ck_stored_emails_occurrence_complete",
                table: "stored_emails");

            migrationBuilder.AlterColumn<long>(
                name: "UidValidity",
                table: "stored_emails",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "Uid",
                table: "stored_emails",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}

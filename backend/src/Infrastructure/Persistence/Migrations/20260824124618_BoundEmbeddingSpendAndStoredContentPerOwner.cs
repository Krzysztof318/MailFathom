// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BoundEmbeddingSpendAndStoredContentPerOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_embedding_spend_periods",
                table: "embedding_spend_periods");

            // Added nullable and filled before it is closed, rather than with a default: a default would attribute
            // every period this deployment has already spent to nobody, and the zero identifier is not a row anybody
            // could later reconcile against.
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "embedding_spend_periods",
                type: "uuid",
                nullable: true);

            // Every character this deployment has spent was spent for one owner, because that is what a deployment
            // served before the release that introduced the owner record. The subquery names that row rather than a
            // value repeated here, exactly as the migration which carried the mail accounts onto it does.
            migrationBuilder.Sql(
                """
                UPDATE embedding_spend_periods
                SET "OwnerId" = (SELECT "Id" FROM settings_accounts ORDER BY "CreatedAt", "Id" LIMIT 1)
                WHERE "OwnerId" IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerId",
                table: "embedding_spend_periods",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_embedding_spend_periods",
                table: "embedding_spend_periods",
                columns: new[] { "PeriodStartsAt", "OwnerId" });

            migrationBuilder.CreateTable(
                name: "owner_stored_content",
                columns: table => new
                {
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoredContentByteCount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_owner_stored_content", x => x.OwnerId);
                    table.ForeignKey(
                        name: "FK_owner_stored_content_settings_accounts_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "settings_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // The counter starts from what the payloads already hold rather than from zero, because the ceiling it
            // serves is consulted before every message: an owner whose mailbox was already stored would otherwise be
            // admitted a second mailbox's worth on the strength of a figure that only meant nobody had written it. This
            // is the same sum the port re-derives with, and it is one statement over the whole deployment rather than
            // one per owner. An owner holding no content at all gets no row and is derived on their first read. It scans
            // the content table once, reading the recorded length rather than the payload beside it, so a large mailbox
            // costs a sequential scan at upgrade and detoasts nothing.
            migrationBuilder.Sql(
                """
                INSERT INTO owner_stored_content ("OwnerId", "StoredContentByteCount")
                SELECT account."OwnerId", SUM(content."MimeByteLength")
                FROM email_message_contents AS content
                JOIN stored_emails AS email
                    ON email."Id" = content."StoredEmailId"
                JOIN mailbox_accounts AS account
                    ON account."Id" = email."MailboxAccountId"
                GROUP BY account."OwnerId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "owner_stored_content");

            migrationBuilder.DropPrimaryKey(
                name: "PK_embedding_spend_periods",
                table: "embedding_spend_periods");

            // What each owner spent is added onto one row per period before the others are removed, because the key
            // restored below cannot hold two rows naming the same period: dropping the column first would leave the
            // schema unable to take its own primary key back on any deployment that served more than one owner. The
            // row kept is the one whose owner sorts first, which makes the choice deterministic rather than whichever
            // the server returned.
            migrationBuilder.Sql(
                """
                UPDATE embedding_spend_periods AS survivor
                SET "ConsumedInputCharacterCount" = totals.total
                FROM (
                    SELECT "PeriodStartsAt", SUM("ConsumedInputCharacterCount") AS total
                    FROM embedding_spend_periods
                    GROUP BY "PeriodStartsAt"
                ) AS totals
                WHERE survivor."PeriodStartsAt" = totals."PeriodStartsAt"
                  AND survivor."OwnerId" = (
                      SELECT charged."OwnerId"
                      FROM embedding_spend_periods AS charged
                      WHERE charged."PeriodStartsAt" = survivor."PeriodStartsAt"
                      ORDER BY charged."OwnerId"
                      LIMIT 1);
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM embedding_spend_periods AS superseded
                WHERE superseded."OwnerId" <> (
                    SELECT charged."OwnerId"
                    FROM embedding_spend_periods AS charged
                    WHERE charged."PeriodStartsAt" = superseded."PeriodStartsAt"
                    ORDER BY charged."OwnerId"
                    LIMIT 1);
                """);

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "embedding_spend_periods");

            migrationBuilder.AddPrimaryKey(
                name: "PK_embedding_spend_periods",
                table: "embedding_spend_periods",
                column: "PeriodStartsAt");
        }
    }
}

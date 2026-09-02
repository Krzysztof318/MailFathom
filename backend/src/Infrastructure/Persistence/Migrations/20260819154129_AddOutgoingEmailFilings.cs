// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutgoingEmailFilings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stored_emails_awaiting_rule_evaluation",
                table: "stored_emails");

            migrationBuilder.AddColumn<Guid>(
                name: "FiledFromOutgoingEmailId",
                table: "stored_emails",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastFilingFailureCode",
                table: "outgoing_emails",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "outgoing_email_filings",
                columns: table => new
                {
                    OutgoingEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    Filing = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FolderAlias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FolderPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlacementUidValidity = table.Column<long>(type: "bigint", nullable: true),
                    PlacementUid = table.Column<long>(type: "bigint", nullable: true),
                    InternetMessageId = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: true),
                    AppendedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WithdrawnAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outgoing_email_filings", x => new { x.OutgoingEmailId, x.Filing });
                    table.ForeignKey(
                        name: "fk_outgoing_email_filings_emails",
                        column: x => x.OutgoingEmailId,
                        principalTable: "outgoing_emails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_awaiting_rule_evaluation",
                table: "stored_emails",
                columns: new[] { "MailboxAccountId", "Id" },
                filter: "\"RulesEvaluatedAt\" IS NULL AND \"FiledFromOutgoingEmailId\" IS NULL");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outgoing_email_filings");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_awaiting_rule_evaluation",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "FiledFromOutgoingEmailId",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "LastFilingFailureCode",
                table: "outgoing_emails");

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_awaiting_rule_evaluation",
                table: "stored_emails",
                columns: new[] { "MailboxAccountId", "Id" },
                filter: "\"RulesEvaluatedAt\" IS NULL");
        }
    }
}

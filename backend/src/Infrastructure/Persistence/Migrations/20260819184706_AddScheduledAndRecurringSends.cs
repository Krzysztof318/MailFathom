// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledAndRecurringSends : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DueAt",
                table: "outgoing_emails",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DueZoneId",
                table: "outgoing_emails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recurring_sends",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequesterOrigin = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequesterIdentity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Schedule = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DraftByteLength = table.Column<long>(type: "bigint", nullable: false),
                    DeclaredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastOccurrenceAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastOccurrenceEmailId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_sends", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "recurring_send_drafts",
                columns: table => new
                {
                    RecurringSendId = table.Column<Guid>(type: "uuid", nullable: false),
                    DraftMime = table.Column<byte[]>(type: "bytea", nullable: false),
                    DraftByteLength = table.Column<long>(type: "bigint", nullable: false),
                    Sha256Hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    StoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_send_drafts", x => x.RecurringSendId);
                    table.ForeignKey(
                        name: "fk_recurring_send_drafts_sends",
                        column: x => x.RecurringSendId,
                        principalTable: "recurring_sends",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recurring_send_recipients",
                columns: table => new
                {
                    RecurringSendId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: true),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_send_recipients", x => new { x.RecurringSendId, x.Ordinal });
                    table.ForeignKey(
                        name: "fk_recurring_send_recipients_sends",
                        column: x => x.RecurringSendId,
                        principalTable: "recurring_sends",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recurring_sends_active",
                table: "recurring_sends",
                column: "DeclaredAt",
                filter: "\"CancelledAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_sends_identity",
                table: "recurring_sends",
                columns: new[] { "MailboxAccountId", "RequesterOrigin", "RequesterIdentity" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recurring_send_drafts");

            migrationBuilder.DropTable(
                name: "recurring_send_recipients");

            migrationBuilder.DropTable(
                name: "recurring_sends");

            migrationBuilder.DropColumn(
                name: "DueAt",
                table: "outgoing_emails");

            migrationBuilder.DropColumn(
                name: "DueZoneId",
                table: "outgoing_emails");
        }
    }
}

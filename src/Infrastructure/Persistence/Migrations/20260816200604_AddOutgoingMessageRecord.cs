// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutgoingMessageRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outgoing_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequesterOrigin = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequesterIdentity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MimeByteLength = table.Column<long>(type: "bigint", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StageChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastFailureCode = table.Column<int>(type: "integer", nullable: true),
                    LastReplyCode = table.Column<int>(type: "integer", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outgoing_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outgoing_message_contents",
                columns: table => new
                {
                    OutgoingMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawMime = table.Column<byte[]>(type: "bytea", nullable: false),
                    MimeByteLength = table.Column<long>(type: "bigint", nullable: false),
                    Sha256Hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    StoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outgoing_message_contents", x => x.OutgoingMessageId);
                    table.ForeignKey(
                        name: "FK_outgoing_message_contents_outgoing_messages_OutgoingMessage~",
                        column: x => x.OutgoingMessageId,
                        principalTable: "outgoing_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "outgoing_message_recipients",
                columns: table => new
                {
                    OutgoingMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastReplyCode = table.Column<int>(type: "integer", nullable: true),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outgoing_message_recipients", x => new { x.OutgoingMessageId, x.Ordinal });
                    table.ForeignKey(
                        name: "FK_outgoing_message_recipients_outgoing_messages_OutgoingMessa~",
                        column: x => x.OutgoingMessageId,
                        principalTable: "outgoing_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_messages_identity",
                table: "outgoing_messages",
                columns: new[] { "MailboxAccountId", "RequesterOrigin", "RequesterIdentity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_messages_outstanding",
                table: "outgoing_messages",
                columns: new[] { "MailboxAccountId", "RecordedAt" },
                filter: "\"Stage\" NOT IN ('Sent', 'Refused', 'Cancelled')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outgoing_message_contents");

            migrationBuilder.DropTable(
                name: "outgoing_message_recipients");

            migrationBuilder.DropTable(
                name: "outgoing_messages");
        }
    }
}

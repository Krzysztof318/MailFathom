// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutgoingEmailRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outgoing_emails",
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
                    table.PrimaryKey("PK_outgoing_emails", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outgoing_email_contents",
                columns: table => new
                {
                    OutgoingEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawMime = table.Column<byte[]>(type: "bytea", nullable: false),
                    MimeByteLength = table.Column<long>(type: "bigint", nullable: false),
                    Sha256Hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    StoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outgoing_email_contents", x => x.OutgoingEmailId);
                    table.ForeignKey(
                        name: "fk_outgoing_email_contents_emails",
                        column: x => x.OutgoingEmailId,
                        principalTable: "outgoing_emails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "outgoing_email_recipients",
                columns: table => new
                {
                    OutgoingEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastReplyCode = table.Column<int>(type: "integer", nullable: true),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outgoing_email_recipients", x => new { x.OutgoingEmailId, x.Ordinal });
                    table.ForeignKey(
                        name: "fk_outgoing_email_recipients_emails",
                        column: x => x.OutgoingEmailId,
                        principalTable: "outgoing_emails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outgoing_email_contents");

            migrationBuilder.DropTable(
                name: "outgoing_email_recipients");

            migrationBuilder.DropTable(
                name: "outgoing_emails");
        }
    }
}

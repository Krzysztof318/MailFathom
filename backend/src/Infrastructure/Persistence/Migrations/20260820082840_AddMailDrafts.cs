// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMailDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mail_drafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequesterOrigin = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequesterIdentity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    MimeByteLength = table.Column<long>(type: "bigint", nullable: false),
                    ComposedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevisedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DiscardedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PromotedToOutgoingEmailId = table.Column<Guid>(type: "uuid", nullable: true),
                    DivergenceReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DivergenceObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastFailureCode = table.Column<int>(type: "integer", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mail_drafts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mail_draft_contents",
                columns: table => new
                {
                    MailDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawMime = table.Column<byte[]>(type: "bytea", nullable: false),
                    MimeByteLength = table.Column<long>(type: "bigint", nullable: false),
                    Sha256Hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    StoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mail_draft_contents", x => x.MailDraftId);
                    table.ForeignKey(
                        name: "fk_mail_draft_contents_drafts",
                        column: x => x.MailDraftId,
                        principalTable: "mail_drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mail_draft_copies",
                columns: table => new
                {
                    MailDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    FolderAlias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FolderPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlacementUidValidity = table.Column<long>(type: "bigint", nullable: true),
                    PlacementUid = table.Column<long>(type: "bigint", nullable: true),
                    InternetMessageId = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: true),
                    AppendedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SettledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mail_draft_copies", x => new { x.MailDraftId, x.Revision });
                    table.ForeignKey(
                        name: "fk_mail_draft_copies_drafts",
                        column: x => x.MailDraftId,
                        principalTable: "mail_drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mail_draft_recipients",
                columns: table => new
                {
                    MailDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: true),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mail_draft_recipients", x => new { x.MailDraftId, x.Ordinal });
                    table.ForeignKey(
                        name: "fk_mail_draft_recipients_drafts",
                        column: x => x.MailDraftId,
                        principalTable: "mail_drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mail_drafts_account_revised",
                table: "mail_drafts",
                columns: new[] { "MailboxAccountId", "RevisedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_drafts_promoted",
                table: "mail_drafts",
                column: "PromotedToOutgoingEmailId",
                filter: "\"PromotedToOutgoingEmailId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mail_draft_contents");

            migrationBuilder.DropTable(
                name: "mail_draft_copies");

            migrationBuilder.DropTable(
                name: "mail_draft_recipients");

            migrationBuilder.DropTable(
                name: "mail_drafts");
        }
    }
}

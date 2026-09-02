// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftSubjectAndStagedAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Subject",
                table: "mail_drafts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "mail_draft_attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MailDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ByteLength = table.Column<long>(type: "bigint", nullable: false),
                    StagedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mail_draft_attachments", x => x.Id);
                    table.ForeignKey(
                        name: "fk_mail_draft_attachments_drafts",
                        column: x => x.MailDraftId,
                        principalTable: "mail_drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mail_draft_attachment_contents",
                columns: table => new
                {
                    MailDraftAttachmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mail_draft_attachment_contents", x => x.MailDraftAttachmentId);
                    table.ForeignKey(
                        name: "fk_mail_draft_attachment_contents_attachments",
                        column: x => x.MailDraftAttachmentId,
                        principalTable: "mail_draft_attachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mail_draft_attachments_draft_staged",
                table: "mail_draft_attachments",
                columns: new[] { "MailDraftId", "StagedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mail_draft_attachment_contents");

            migrationBuilder.DropTable(
                name: "mail_draft_attachments");

            migrationBuilder.DropColumn(
                name: "Subject",
                table: "mail_drafts");
        }
    }
}

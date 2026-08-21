// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMailboxMutations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mailbox_mutations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MailFolderId = table.Column<long>(type: "bigint", nullable: false),
                    UidValidity = table.Column<long>(type: "bigint", nullable: false),
                    Uid = table.Column<long>(type: "bigint", nullable: false),
                    Mutation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequesterOrigin = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequesterIdentity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DestinationFolderPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DestinationHierarchyDelimiter = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: true),
                    DesiredSeenState = table.Column<bool>(type: "boolean", nullable: true),
                    Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlacementUidValidity = table.Column<long>(type: "bigint", nullable: true),
                    PlacementUid = table.Column<long>(type: "bigint", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StageChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastFailureCode = table.Column<int>(type: "integer", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mailbox_mutations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mailbox_mutations_mail_folders_MailFolderId",
                        column: x => x.MailFolderId,
                        principalTable: "mail_folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_mailbox_mutations_stored_emails_StoredEmailId",
                        column: x => x.StoredEmailId,
                        principalTable: "stored_emails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_mutations_identity",
                table: "mailbox_mutations",
                columns: new[] { "MailFolderId", "UidValidity", "Uid", "RequesterOrigin", "RequesterIdentity", "Mutation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_mutations_outstanding",
                table: "mailbox_mutations",
                columns: new[] { "MailboxAccountId", "RecordedAt" },
                filter: "\"Stage\" <> 'Completed'");

            migrationBuilder.CreateIndex(
                name: "IX_mailbox_mutations_StoredEmailId",
                table: "mailbox_mutations",
                column: "StoredEmailId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mailbox_mutations");
        }
    }
}

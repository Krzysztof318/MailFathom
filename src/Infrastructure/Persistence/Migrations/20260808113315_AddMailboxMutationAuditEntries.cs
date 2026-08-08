// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMailboxMutationAuditEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AuditTrailEnabled",
                table: "mailbox_mutations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "mailbox_mutation_audit_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MutationRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mutation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceFolderPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SourceHierarchyDelimiter = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: true),
                    SourceUidValidity = table.Column<long>(type: "bigint", nullable: false),
                    SourceUid = table.Column<long>(type: "bigint", nullable: false),
                    DestinationFolderPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DestinationHierarchyDelimiter = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: true),
                    PlacementUidValidity = table.Column<long>(type: "bigint", nullable: true),
                    PlacementUid = table.Column<long>(type: "bigint", nullable: true),
                    DesiredSeenState = table.Column<bool>(type: "boolean", nullable: true),
                    RequesterOrigin = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequesterIdentity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FailureCode = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mailbox_mutation_audit_entries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_mutation_audit_entries_account_completed",
                table: "mailbox_mutation_audit_entries",
                columns: new[] { "MailboxAccountId", "CompletedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_mutation_audit_entries_mutation",
                table: "mailbox_mutation_audit_entries",
                column: "MutationRecordId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mailbox_mutation_audit_entries");

            migrationBuilder.DropColumn(
                name: "AuditTrailEnabled",
                table: "mailbox_mutations");
        }
    }
}

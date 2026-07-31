// Copyright © 2026 Krzysztof Kasprowicz

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;

#nullable disable

namespace MailMcp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "backfill_positions",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastProcessedStoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backfill_positions", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "mailbox_accounts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mailbox_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mail_folders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Alias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResolutionGeneration = table.Column<int>(type: "integer", nullable: false),
                    RemotePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    HierarchyDelimiter = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mail_folders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mail_folders_mailbox_accounts_MailboxAccountId",
                        column: x => x.MailboxAccountId,
                        principalTable: "mailbox_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stored_emails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MailFolderId = table.Column<long>(type: "bigint", nullable: false),
                    UidValidity = table.Column<long>(type: "bigint", nullable: false),
                    Uid = table.Column<long>(type: "bigint", nullable: false),
                    InternetMessageId = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: true),
                    Subject = table.Column<string>(type: "text", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SizeOctets = table.Column<long>(type: "bigint", nullable: false),
                    ContentAvailability = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SenderDisplayName = table.Column<string>(type: "text", nullable: true),
                    SenderAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    SenderNormalizedAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    ToAddresses = table.Column<string[]>(type: "text[]", nullable: false),
                    CcAddresses = table.Column<string[]>(type: "text[]", nullable: false),
                    ReplyToAddresses = table.Column<string[]>(type: "text[]", nullable: false),
                    InReplyTo = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: true),
                    ThreadReferences = table.Column<string[]>(type: "text[]", nullable: false),
                    AttachmentCount = table.Column<int>(type: "integer", nullable: false),
                    AttachmentTotalSizeOctets = table.Column<long>(type: "bigint", nullable: false),
                    InlineResourceCount = table.Column<int>(type: "integer", nullable: false),
                    IsEncrypted = table.Column<bool>(type: "boolean", nullable: false),
                    CarriesUnverifiedSignature = table.Column<bool>(type: "boolean", nullable: false),
                    ContainsUnexpandedTnefPart = table.Column<bool>(type: "boolean", nullable: false),
                    RemoteFlagsObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RemoteExpungeObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsRemotelySeen = table.Column<bool>(type: "boolean", nullable: false),
                    IsRemotelyAnswered = table.Column<bool>(type: "boolean", nullable: false),
                    IsRemotelyFlagged = table.Column<bool>(type: "boolean", nullable: false),
                    IsRemotelyDraft = table.Column<bool>(type: "boolean", nullable: false),
                    IsRemotelyDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stored_emails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stored_emails_mail_folders_MailFolderId",
                        column: x => x.MailFolderId,
                        principalTable: "mail_folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "synchronization_checkpoints",
                columns: table => new
                {
                    MailFolderId = table.Column<long>(type: "bigint", nullable: false),
                    UidValidity = table.Column<long>(type: "bigint", nullable: false),
                    LastSeenUid = table.Column<long>(type: "bigint", nullable: true),
                    SynchronizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_synchronization_checkpoints", x => x.MailFolderId);
                    table.ForeignKey(
                        name: "FK_synchronization_checkpoints_mail_folders_MailFolderId",
                        column: x => x.MailFolderId,
                        principalTable: "mail_folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "email_content_repair_requests",
                columns: table => new
                {
                    StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    Defect = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FirstRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RequestCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_content_repair_requests", x => x.StoredEmailId);
                    table.ForeignKey(
                        name: "FK_email_content_repair_requests_stored_emails_StoredEmailId",
                        column: x => x.StoredEmailId,
                        principalTable: "stored_emails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "email_message_contents",
                columns: table => new
                {
                    StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawMime = table.Column<byte[]>(type: "bytea", nullable: false),
                    MimeByteLength = table.Column<long>(type: "bigint", nullable: false),
                    Sha256Hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    StoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_message_contents", x => x.StoredEmailId);
                    table.ForeignKey(
                        name: "FK_email_message_contents_stored_emails_StoredEmailId",
                        column: x => x.StoredEmailId,
                        principalTable: "stored_emails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "email_search_documents",
                columns: table => new
                {
                    StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ParticipantAddresses = table.Column<string>(type: "text", nullable: true),
                    BodyText = table.Column<string>(type: "text", nullable: true),
                    BodyTextBeforeTrimming = table.Column<string>(type: "text", nullable: true),
                    TextSource = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExtractedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: false)
                        .Annotation("Npgsql:TsVectorConfig", "simple")
                        .Annotation("Npgsql:TsVectorProperties", new[] { "SubjectText", "ParticipantAddresses", "BodyText" })
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_search_documents", x => x.StoredEmailId);
                    table.ForeignKey(
                        name: "FK_email_search_documents_stored_emails_StoredEmailId",
                        column: x => x.StoredEmailId,
                        principalTable: "stored_emails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_search_documents_search_vector",
                table: "email_search_documents",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "ix_mail_folders_account_alias_generation",
                table: "mail_folders",
                columns: new[] { "MailboxAccountId", "Alias", "ResolutionGeneration" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_account_timeline",
                table: "stored_emails",
                columns: new[] { "MailboxAccountId", "ReceivedAt", "Id" },
                descending: new[] { false, true, true })
                .Annotation("Npgsql:IndexNullSortOrder", new[] { NullSortOrder.Unspecified, NullSortOrder.NullsLast, NullSortOrder.Unspecified });

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_cc_addresses",
                table: "stored_emails",
                column: "CcAddresses")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_folder_timeline",
                table: "stored_emails",
                columns: new[] { "MailFolderId", "ReceivedAt", "Id" },
                descending: new[] { false, true, true })
                .Annotation("Npgsql:IndexNullSortOrder", new[] { NullSortOrder.Unspecified, NullSortOrder.NullsLast, NullSortOrder.Unspecified });

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_folder_uidvalidity_uid",
                table: "stored_emails",
                columns: new[] { "MailFolderId", "UidValidity", "Uid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_reconciliation_queue",
                table: "stored_emails",
                columns: new[] { "MailFolderId", "RemoteFlagsObservedAt" })
                .Annotation("Npgsql:IndexNullSortOrder", new[] { NullSortOrder.Unspecified, NullSortOrder.NullsFirst });

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_reply_to_addresses",
                table: "stored_emails",
                column: "ReplyToAddresses")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_sender",
                table: "stored_emails",
                column: "SenderNormalizedAddress");

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_to_addresses",
                table: "stored_emails",
                column: "ToAddresses")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "backfill_positions");

            migrationBuilder.DropTable(
                name: "email_content_repair_requests");

            migrationBuilder.DropTable(
                name: "email_message_contents");

            migrationBuilder.DropTable(
                name: "email_search_documents");

            migrationBuilder.DropTable(
                name: "synchronization_checkpoints");

            migrationBuilder.DropTable(
                name: "stored_emails");

            migrationBuilder.DropTable(
                name: "mail_folders");

            migrationBuilder.DropTable(
                name: "mailbox_accounts");
        }
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Source = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TargetKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetStoredEmailId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetScreen = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeduplicationKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notifications_settings_accounts_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "settings_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_notifications_stored_emails_TargetStoredEmailId",
                        column: x => x.TargetStoredEmailId,
                        principalTable: "stored_emails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_owner_occurred",
                table: "notifications",
                columns: new[] { "OwnerId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_owner_unread_condition",
                table: "notifications",
                columns: new[] { "OwnerId", "DeduplicationKey" },
                unique: true,
                filter: "NOT \"IsRead\"");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_TargetStoredEmailId",
                table: "notifications",
                column: "TargetStoredEmailId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notifications");
        }
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveUserPortraitsIntoStoredFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stored_files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ByteLength = table.Column<long>(type: "bigint", nullable: false),
                    Sha256Hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    Backend = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "Database"),
                    Content = table.Column<byte[]>(type: "bytea", nullable: true),
                    ObjectLocator = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ObjectVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stored_files", x => x.Id);
                    table.CheckConstraint("ck_stored_files_backend_payload", "(\"Backend\" = 'Database' AND \"Content\" IS NOT NULL AND \"ObjectLocator\" IS NULL AND \"ObjectVerifiedAt\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL\n    AND (\"Content\" IS NULL OR \"ObjectVerifiedAt\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "fk_stored_files_settings_accounts",
                        column: x => x.UserId,
                        principalTable: "settings_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stored_files_object_locator",
                table: "stored_files",
                column: "ObjectLocator",
                unique: true,
                filter: "\"Backend\" = 'ObjectStorage'");

            migrationBuilder.CreateIndex(
                name: "IX_stored_files_UserId",
                table: "stored_files",
                column: "UserId");

            // Every portrait is carried into the database backend, whatever ContentStorage selects: the move is what
            // takes a payload to the object backend, and it reaches stored files like any other kind. The kind was
            // proven from the signature when the row was written, so the first three octets decide the media type. The
            // link is written into the record and its version advanced, so a buffer opened over the old record is
            // refused rather than saved over the link.
            migrationBuilder.Sql(
                """
                WITH carried AS (
                    INSERT INTO stored_files ("Id", "UserId", "MediaType", "ByteLength", "Sha256Hash", "Backend", "Content", "CreatedAt")
                    SELECT gen_random_uuid(),
                           p."UserId",
                           CASE WHEN substring(p."Content" FROM 1 FOR 3) = '\xFFD8FF'::bytea THEN 'image/jpeg' ELSE 'image/png' END,
                           length(p."Content"),
                           sha256(p."Content"),
                           'Database',
                           p."Content",
                           p."UpdatedAt"
                    FROM user_portraits AS p
                    RETURNING "Id", "UserId"
                )
                UPDATE settings_accounts AS u
                SET "Document" = jsonb_set(u."Document", '{Portrait}', to_jsonb(c."Id"::text)),
                    "Version" = u."Version" + 1
                FROM carried AS c
                WHERE u."Id" = c."UserId";
                """);

            migrationBuilder.DropTable(
                name: "user_portraits");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_portraits",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_portraits", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_user_portraits_settings_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "settings_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Only a portrait whose octets the database still holds can be carried back; one already moved into the
            // object backend has nowhere in the older schema to be read from, and is dropped with its link.
            migrationBuilder.Sql(
                """
                INSERT INTO user_portraits ("UserId", "Content", "CreatedAt", "UpdatedAt")
                SELECT f."UserId", f."Content", f."CreatedAt", f."CreatedAt"
                FROM stored_files AS f
                JOIN settings_accounts AS u ON u."Id" = f."UserId"
                WHERE f."Content" IS NOT NULL
                  AND lower(u."Document" ->> 'Portrait') = f."Id"::text;

                UPDATE settings_accounts
                SET "Document" = "Document" - 'Portrait',
                    "Version" = "Version" + 1
                WHERE "Document" ? 'Portrait';
                """);

            migrationBuilder.DropTable(
                name: "stored_files");
        }
    }
}

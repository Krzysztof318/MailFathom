// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailThreadStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_thread_states",
                columns: table => new
                {
                    EmailThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    Coverage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DerivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DerivedFromMessageCount = table.Column<int>(type: "integer", nullable: false),
                    DerivedFromLatestArrival = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_thread_states", x => x.EmailThreadId);
                    table.ForeignKey(
                        name: "FK_email_thread_states_email_threads_EmailThreadId",
                        column: x => x.EmailThreadId,
                        principalTable: "email_threads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "email_thread_state_entries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmailThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    Aspect = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    OwedBy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Sources = table.Column<Guid[]>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_thread_state_entries", x => x.Id);
                    table.ForeignKey(
                        name: "fk_email_thread_state_entries_states",
                        column: x => x.EmailThreadId,
                        principalTable: "email_thread_states",
                        principalColumn: "EmailThreadId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_email_thread_state_entries_EmailThreadId_Aspect_Ordinal",
                table: "email_thread_state_entries",
                columns: new[] { "EmailThreadId", "Aspect", "Ordinal" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_thread_state_entries");

            migrationBuilder.DropTable(
                name: "email_thread_states");
        }
    }
}

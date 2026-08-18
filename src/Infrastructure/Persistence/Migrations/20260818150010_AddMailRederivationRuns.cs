// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMailRederivationRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mail_rederivation_runs",
                columns: table => new
                {
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FolderAlias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SegmentCount = table.Column<int>(type: "integer", nullable: false),
                    RederivedEmailCount = table.Column<int>(type: "integer", nullable: false),
                    UnreadableEmailCount = table.Column<int>(type: "integer", nullable: false),
                    MissingContentEmailCount = table.Column<int>(type: "integer", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mail_rederivation_runs", x => new { x.MailboxAccountId, x.FolderAlias });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mail_rederivation_runs");
        }
    }
}

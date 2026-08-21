// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbeddingSpendPeriodsAndChunkedTextTruncation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChunkedTextTruncatedFromCharacterCount",
                table: "stored_emails",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "embedding_spend_periods",
                columns: table => new
                {
                    PeriodStartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedInputCharacterCount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_embedding_spend_periods", x => x.PeriodStartsAt);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "embedding_spend_periods");

            migrationBuilder.DropColumn(
                name: "ChunkedTextTruncatedFromCharacterCount",
                table: "stored_emails");
        }
    }
}

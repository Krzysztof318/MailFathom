// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailThreadSurvivorForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_email_threads_MergedIntoEmailThreadId",
                table: "email_threads",
                column: "MergedIntoEmailThreadId");

            migrationBuilder.AddForeignKey(
                name: "FK_email_threads_email_threads_MergedIntoEmailThreadId",
                table: "email_threads",
                column: "MergedIntoEmailThreadId",
                principalTable: "email_threads",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_email_threads_email_threads_MergedIntoEmailThreadId",
                table: "email_threads");

            migrationBuilder.DropIndex(
                name: "IX_email_threads_MergedIntoEmailThreadId",
                table: "email_threads");
        }
    }
}

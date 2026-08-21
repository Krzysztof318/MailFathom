// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceSenderDomainAlignmentWithAuthorAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SenderDomainAlignment",
                table: "stored_emails");

            migrationBuilder.AddColumn<string>(
                name: "AuthenticatedAuthorDomain",
                table: "stored_emails",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorAuthenticationOutcome",
                table: "stored_emails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "NotEstablished");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthenticatedAuthorDomain",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "AuthorAuthenticationOutcome",
                table: "stored_emails");

            migrationBuilder.AddColumn<string>(
                name: "SenderDomainAlignment",
                table: "stored_emails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "NotAssessed");
        }
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStoredEmailSenderAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthenticatedSenderDomain",
                table: "stored_emails",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DkimSignerDomain",
                table: "stored_emails",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DmarcOutcome",
                table: "stored_emails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "NotReported");

            migrationBuilder.AddColumn<string>(
                name: "SenderAuthenticationMethod",
                table: "stored_emails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "SenderAuthenticationOutcome",
                table: "stored_emails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "NotEstablished");

            migrationBuilder.AddColumn<string>(
                name: "SenderDomainAlignment",
                table: "stored_emails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "NotAssessed");

            migrationBuilder.AddColumn<string>(
                name: "SpfMailFromDomain",
                table: "stored_emails",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthenticatedSenderDomain",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "DkimSignerDomain",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "DmarcOutcome",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SenderAuthenticationMethod",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SenderAuthenticationOutcome",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SenderDomainAlignment",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SpfMailFromDomain",
                table: "stored_emails");
        }
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// SQL only and no model change, deliberately: the two endpoint columns and the record beside them are the same
    /// before and after. What changes is which of them is the source. A user's row is rewritten from what their record
    /// resolves to by every commit that can move that answer, so a switch a row holds off while the record says
    /// nothing about it would be turned on by the first such commit. The default administrator is the row this
    /// exists for: its switches were written onto the row alone.
    /// </remarks>
    public partial class StateEndpointSwitchesHeldOffOnUserRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only a switch that is off is stated, so one that is on stays a setting a policy's default may still
            // supply. A record already carrying the block under any casing has said what it means and is left as it
            // stands, and so is a row whose document is not an object, which no build reads as a record. The version
            // moves because the record did: every replica composes the user again, and an edit composed over the
            // earlier document is refused rather than committed without the block.
            migrationBuilder.Sql(
                """
                UPDATE settings_accounts
                SET "Document" = "Document" || jsonb_build_object(
                        'EndpointAccess',
                        jsonb_strip_nulls(jsonb_build_object(
                            'McpEndpoint', CASE WHEN NOT "McpEndpointEnabled" THEN 'false' END,
                            'ClientEndpoint', CASE WHEN NOT "ClientEndpointEnabled" THEN 'false' END))),
                    "Version" = "Version" + 1
                WHERE jsonb_typeof("Document") = 'object'
                  AND NOT ("McpEndpointEnabled" AND "ClientEndpointEnabled")
                  AND NOT EXISTS (
                      SELECT 1 FROM jsonb_object_keys("Document") AS stated(key)
                      WHERE lower(stated.key) = 'endpointaccess');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to take back. The block states what the row already held, an earlier build reads it as it
            // reads any record's, and removing it could not tell a block this wrote from one somebody stated since.
        }
    }
}

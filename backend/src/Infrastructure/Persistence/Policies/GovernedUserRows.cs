// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Persistence.Users;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Policies;

/// <summary>Composes the read of user rows a write that changes their governing policies judges.</summary>
[RequiresIntegrationCoverage]
internal static class GovernedUserRows
{
    /// <summary>Composes the bounded read of every user's row, which a caller narrows to the users its write governs.</summary>
    /// <param name="dbContext">The context the query is composed over, inside the write's transaction.</param>
    /// <returns>Each user's envelope and switches, and their own record where it is within what this build reads one from.</returns>
    /// <remarks>
    /// The bound a read of one user's record is held to, applied in the statement for the same reason: a row past it
    /// is one something went wrong with, and transferring a gigabyte to find that out is the cost the bound exists
    /// to refuse.
    /// </remarks>
    internal static IQueryable<StoredGovernedUserRow> Of(MailFathomDbContext dbContext) =>
        dbContext.Database.SqlQuery<StoredGovernedUserRow>(
            $"""
            SELECT
                "Id",
                "DisplayName",
                "OrganizationId",
                CASE WHEN octet_length("Document"::text) <= {UserSettingsDocument.MaximumOctets} THEN "Document"::text END AS "Document",
                "McpEndpointEnabled",
                "ClientEndpointEnabled"
            FROM settings_accounts
            """);
}

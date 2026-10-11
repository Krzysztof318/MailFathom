// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Persistence.Users;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Policies.MailAccounts;

/// <summary>Composes the read of mail-account rows a write that changes their governing policies judges.</summary>
[RequiresIntegrationCoverage]
internal static class GovernedMailAccountRows
{
    /// <summary>Composes the bounded read of every mail account's row, which a caller narrows to the accounts its write governs.</summary>
    /// <param name="dbContext">The context the query is composed over, inside the write's transaction.</param>
    /// <returns>Each account's columns and assignees, and its own settings where they are within what this build binds an account from.</returns>
    /// <remarks>The bound a read of one user's accounts is held to, applied in the statement for the reason <see cref="GovernedUserRows" /> gives.</remarks>
    internal static IQueryable<StoredGovernedMailAccountRow> Of(MailFathomDbContext dbContext) =>
        dbContext.Database.SqlQuery<StoredGovernedMailAccountRow>(
            $"""
            SELECT
                account."Id",
                account."EmailAddress",
                account."DisplayName",
                account."OrganizationId",
                CASE WHEN octet_length(account."Document"::text) <= {UserSettingsDocument.MaximumOctets} THEN account."Document"::text END AS "Document",
                account."Version",
                ARRAY(
                    SELECT assignment."UserId"
                    FROM mail_account_assignments AS assignment
                    WHERE assignment."MailAccountId" = account."Id"
                    ORDER BY assignment."AssignedAt") AS "Users"
            FROM settings_mail_accounts AS account
            """);
}

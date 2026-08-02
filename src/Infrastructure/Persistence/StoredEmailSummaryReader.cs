// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence;

/// <summary>Reads one stored email's summary out of PostgreSQL by its primary key.</summary>
/// <remarks>
/// <para>
/// The lookup is a projection rather than a <c>FindAsync</c>, which is the privacy control the listing query applies for
/// the same reason: the query names the columns a summary publishes, so nothing here can reach the stored raw MIME, and
/// no entity enters the change tracker on a path that only reads.
/// </para>
/// <para>
/// A tombstone answers as an absent email, which is what keeps the content read consistent with the listing that led a
/// caller to it. It also makes the identifier of a deleted email indistinguishable from one that never existed, on the
/// same terms as an account this deployment no longer serves.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailSummaryReader(MailFathomDbContext dbContext) : IStoredEmailSummaryReader
{
    /// <inheritdoc />
    public async Task<EmailSummary?> FindAsync(StoredEmailId storedEmailId, CancellationToken cancellationToken)
    {
        var row = await dbContext.StoredEmails
            .AsNoTracking()
            .Where(email => email.Id == storedEmailId.Value && email.RemoteExpungeObservedAt == null)
            .Select(StoredEmailSummaryRow.Projection)
            .SingleOrDefaultAsync(cancellationToken);

        return row?.ToSummary();
    }
}

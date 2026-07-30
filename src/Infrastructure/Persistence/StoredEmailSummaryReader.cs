// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Emails;
using MailMcp.CodeCoverage;
using MailMcp.Domain.Emails;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>Reads one stored email's summary out of PostgreSQL by its primary key.</summary>
/// <remarks>
/// The lookup is a projection rather than a <c>FindAsync</c>, which is the privacy control the listing query applies for
/// the same reason: the query names the columns a summary publishes, so nothing here can reach the stored raw MIME, and
/// no entity enters the change tracker on a path that only reads.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailSummaryReader(MailMcpDbContext dbContext) : IStoredEmailSummaryReader
{
    /// <inheritdoc />
    public async Task<EmailSummary?> FindAsync(StoredEmailId storedEmailId, CancellationToken cancellationToken)
    {
        var row = await dbContext.StoredEmails
            .AsNoTracking()
            .Where(email => email.Id == storedEmailId.Value)
            .Select(StoredEmailSummaryRow.Projection)
            .SingleOrDefaultAsync(cancellationToken);

        return row?.ToSummary();
    }
}

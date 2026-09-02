// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Owners;

/// <summary>EF Core read of whose mail a background unit of work is acting on.</summary>
/// <remarks>
/// The owner is a column of <c>stored_emails</c> itself, carried down from the folder the message was synchronized
/// into, so the read is a lookup on the message's own primary key and reaches no other table. Nothing is materialized:
/// what a bound needs is the identity, and reading the row would put a message into the change tracker of a session
/// that is about to write about it.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersistedMailOwnership(MailFathomDbContext dbContext) : IMailOwnership
{
    /// <inheritdoc />
    public async Task<MailOwnerId> ReadStoredEmailOwnerAsync(
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        var storedId = storedEmailId.Value;

        var ownerId = await dbContext.StoredEmails
            .AsNoTracking()
            .Where(email => email.Id == storedId)
            .Select(email => (Guid?)email.OwnerId)
            .SingleOrDefaultAsync(cancellationToken);

        // A message that is not there is a defect rather than a state: whatever is asking holds an identifier it read
        // from this database, so an absent row means the message was erased under it.
        return ownerId is { } owner
            ? MailOwnerId.Create(owner)
            : throw new InvalidOperationException(
                $"No message is stored under '{storedEmailId.Value}', so the owner whose bound its work is charged to cannot be established.");
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Owners;

/// <summary>EF Core reads of whose mail a background unit of work is acting on.</summary>
/// <remarks>
/// Both reads end at <c>mailbox_accounts</c>, which is the one table carrying the owner column, and each is a lookup on
/// an index the schema already holds. Neither materializes an entity: what a bound needs is the identity, and reading
/// the account row would put a mailbox into the change tracker of a session that is about to write mail into it.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersistedMailOwnership(MailFathomDbContext dbContext) : IMailOwnership
{
    /// <inheritdoc />
    public async Task<MailOwnerId> ReadAccountOwnerAsync(
        MailAccountId accountId,
        CancellationToken cancellationToken)
    {
        var storedAccountId = accountId.Value;

        var ownerId = await dbContext.MailboxAccounts
            .AsNoTracking()
            .Where(account => account.Id == storedAccountId)
            .Select(account => (Guid?)account.OwnerId)
            .SingleOrDefaultAsync(cancellationToken);

        // An account served but never synchronized has no row until a folder of it is bound, and the row that binding
        // writes takes its owner from exactly this resolution. Falling back to it rather than refusing is what makes a
        // bound hold on an account's first run as well as on its hundredth.
        return MailOwnerId.Create(
            ownerId ?? await OwnerAccountResolver.ResolveConfiguredOwnerAsync(dbContext, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<MailOwnerId> ReadStoredEmailOwnerAsync(
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        var storedId = storedEmailId.Value;

        var ownerId = await dbContext.StoredEmails
            .AsNoTracking()
            .Where(email => email.Id == storedId)
            .Join(
                dbContext.MailboxAccounts,
                email => email.MailboxAccountId,
                account => account.Id,
                (_, account) => (Guid?)account.OwnerId)
            .SingleOrDefaultAsync(cancellationToken);

        // Unlike an account, a message that is not there is a defect rather than a state: whatever is asking holds an
        // identifier it read from this database, so an absent row means the message was erased under it.
        return ownerId is { } owner
            ? MailOwnerId.Create(owner)
            : throw new InvalidOperationException(
                $"No message is stored under '{storedEmailId.Value}', so the owner whose bound its work is charged to cannot be established.");
    }
}

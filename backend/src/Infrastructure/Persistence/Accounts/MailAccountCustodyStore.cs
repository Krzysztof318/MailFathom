// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Accounts;

/// <summary>Reads and writes the two custody columns of the account row.</summary>
/// <remarks>
/// A phase move is a compare-and-set against the phase the caller decided from rather than a plain write, because an
/// account's supervision can cross a lease handover: the replica that read the phase is not necessarily the one still
/// holding the account when the write goes out, and a plain write would then overwrite whatever its replacement had
/// since decided. It is the statement itself that compares, rather than a reading in front of a write: the row's only
/// concurrency token is the folder revision, which a phase move neither reads nor moves, so a comparison made in
/// memory would leave both replicas' writes landing.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailAccountCustodyStore(MailFathomDbContext dbContext) : IMailAccountCustodyStore
{
    /// <inheritdoc />
    public async Task<MailAccountCustodyState?> ReadAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        var accountIdValue = account.Value;

        return await dbContext.MailboxAccounts
            .AsNoTracking()
            .Where(row => row.Id == accountIdValue)
            .Select(static row => new MailAccountCustodyState(row.RequestedCustody, row.CustodyPhase)
            {
                RestoreGeneration = row.RestoreGeneration,
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MailAccountCustodyState?> RequestAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailAccountCustody requested,
        CancellationToken cancellationToken)
    {
        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var accountRow = await sessionContext.MailboxAccounts.FindAsync([account.Value], cancellationToken);

        if (accountRow is null)
        {
            return null;
        }

        var previous = new MailAccountCustodyState(accountRow.RequestedCustody, accountRow.CustodyPhase)
        {
            RestoreGeneration = accountRow.RestoreGeneration,
        };
        accountRow.RequestedCustody = requested;

        return previous;
    }

    /// <inheritdoc />
    public async Task<bool> MovePhaseAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailAccountCustodyPhase decidedFrom,
        MailAccountCustodyPhase moveTo,
        CancellationToken cancellationToken)
    {
        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        var accountIdValue = account.Value;

        var moving = sessionContext.MailboxAccounts
            .Where(row => row.Id == accountIdValue && row.CustodyPhase == decidedFrom);

        // Entering Restoring is what names a restore, and it names it by advancing a counter in the statement that
        // moves the phase: the move is conditional on the phase it was decided from, so exactly one replica's attempt
        // writes and the generation cannot be handed out twice. Nothing ever clears it, because its whole job is to
        // differ from every earlier value this account has used.
        //
        // Leaving the phase owes no walk position, and clearing it here is what makes a second restore walk the
        // account's mail from the start. Nothing else clears that column — the walk itself only advances it — so a
        // cursor left behind by one restore would silently skip every message of the next whose identity sorts at or
        // below it.
        var affected = moveTo is MailAccountCustodyPhase.Restoring
            ? await moving.ExecuteUpdateAsync(
                row => row
                    .SetProperty(entity => entity.CustodyPhase, moveTo)
                    .SetProperty(entity => entity.RestoreGeneration, entity => entity.RestoreGeneration + 1),
                cancellationToken)
            : await moving.ExecuteUpdateAsync(
                row => row
                    .SetProperty(entity => entity.CustodyPhase, moveTo)
                    .SetProperty(entity => entity.RestoreStatePosition, (Guid?)null),
                cancellationToken);

        return affected == 1;
    }
}

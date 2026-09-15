// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds each account's custody in memory, with the compare-and-set the real store answers a lost race with.</summary>
internal sealed class InMemoryMailAccountCustodyStore : IMailAccountCustodyStore
{
    private readonly ConcurrentDictionary<string, MailAccountCustodyState> states = new(StringComparer.Ordinal);

    internal static InMemoryMailAccountCustodyStore Holding(MailAccountId account) =>
        With(account, new MailAccountCustodyState(MailAccountCustody.HoldMailbox, MailAccountCustodyPhase.Held));

    internal static InMemoryMailAccountCustodyStore Mirroring(MailAccountId account) =>
        With(account, MailAccountCustodyState.Mirrored);

    internal static InMemoryMailAccountCustodyStore With(MailAccountId account, MailAccountCustodyState state)
    {
        var store = new InMemoryMailAccountCustodyStore();
        store.states[account.Value] = state;

        return store;
    }

    internal MailAccountCustodyState? StateOf(MailAccountId account) =>
        this.states.GetValueOrDefault(account.Value);

    public Task<MailAccountCustodyState?> ReadAsync(MailAccountId account, CancellationToken cancellationToken) =>
        Task.FromResult(this.states.GetValueOrDefault(account.Value));

    public Task<MailAccountCustodyState?> RequestAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailAccountCustody requested,
        CancellationToken cancellationToken)
    {
        if (!this.states.TryGetValue(account.Value, out var previous))
        {
            return Task.FromResult<MailAccountCustodyState?>(null);
        }

        this.states[account.Value] = previous with { Requested = requested };

        return Task.FromResult<MailAccountCustodyState?>(previous);
    }

    public Task<bool> MovePhaseAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailAccountCustodyPhase decidedFrom,
        MailAccountCustodyPhase moveTo,
        CancellationToken cancellationToken)
    {
        if (!this.states.TryGetValue(account.Value, out var current) || current.Phase != decidedFrom)
        {
            return Task.FromResult(false);
        }

        this.states[account.Value] = current with { Phase = moveTo };

        return Task.FromResult(true);
    }
}

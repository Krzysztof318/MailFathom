// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps one account's stored message state in memory, answering for no other account as the real store answers.</summary>
internal sealed class InMemoryLocalEmailStateStore(MailAccountIdentity account) : ILocalEmailStateStore
{
    private readonly Dictionary<StoredEmailId, LocalEmailState> states = [];

    internal IReadOnlyDictionary<StoredEmailId, LocalEmailState> States => this.states;

    internal List<StoredEmailId> Erased { get; } = [];

    internal void Store(StoredEmailId email, LocalEmailState state) => this.states[email] = state;

    public Task<LocalEmailState?> ReadAsync(
        IPersistenceSession session,
        MailAccountIdentity account1,
        StoredEmailId email,
        CancellationToken cancellationToken) =>
        Task.FromResult(account1 == account ? this.states.GetValueOrDefault(email) : null);

    public Task WriteAsync(
        IPersistenceSession session,
        MailAccountIdentity account1,
        StoredEmailId email,
        LocalEmailState state,
        CancellationToken cancellationToken)
    {
        if (account1 != account || !this.states.ContainsKey(email))
        {
            throw new InvalidOperationException("A held account's change is written only to a message its own read found.");
        }

        this.states[email] = state;

        return Task.CompletedTask;
    }

    public Task EraseAsync(
        IPersistenceSession session,
        MailAccountIdentity account1,
        StoredEmailId email,
        CancellationToken cancellationToken)
    {
        if (account1 == account && this.states.Remove(email))
        {
            this.Erased.Add(email);
        }

        return Task.CompletedTask;
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Users;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>The accounts a deployment holds and the user records they are assigned to, kept in memory.</summary>
/// <remarks>
/// It holds the users' records as well as the accounts, because every account write moves the version of each user it
/// reaches, and a caller reads that version back through the record rather than through the write. A test states a user
/// with <see cref="HoldUser" /> and reads the record the store composes for them with <see cref="DocumentOf" />.
/// </remarks>
internal sealed class InMemoryMailAccountRecordStore : IMailAccountRecordStore
{
    private readonly Dictionary<UserId, HeldUser> users = [];

    private readonly List<MailAccountRecord> accounts = [];

    private readonly Dictionary<Guid, List<UserId>> assignments = [];

    /// <summary>Gets every account the store holds, in the order they were created in.</summary>
    internal IReadOnlyList<MailAccountRecord> Accounts => this.accounts;

    /// <summary>States one user's record and the accounts assigned to them.</summary>
    /// <param name="user">The user.</param>
    /// <param name="json">The record, as the row holds it.</param>
    /// <param name="version">The version the row stands at.</param>
    /// <param name="assigned">The accounts assigned to the user, created when the store does not hold them yet.</param>
    internal void HoldUser(UserId user, string json, long version, params MailAccountRecord[] assigned)
    {
        this.users[user] = new HeldUser(json, version);

        foreach (var account in assigned)
        {
            if (this.accounts.All(held => held.Id != account.Id))
            {
                this.accounts.Add(account);
                this.assignments[account.Id] = [];
            }

            if (!this.assignments[account.Id].Contains(user))
            {
                this.assignments[account.Id].Add(user);
            }
        }
    }

    /// <summary>States an account nobody is assigned.</summary>
    /// <param name="account">The account.</param>
    internal void HoldAccount(MailAccountRecord account)
    {
        this.accounts.Add(account);
        this.assignments[account.Id] = [];
    }

    /// <summary>Composes the record a reader answers for one user, carrying the accounts assigned to them.</summary>
    /// <param name="user">The user.</param>
    /// <returns>The record, or <see langword="null" /> when the store holds no such user.</returns>
    internal UserSettingsDocument? DocumentOf(UserId user) =>
        this.users.TryGetValue(user, out var held)
            ? new UserSettingsDocument(user, $"user-{user.Value:D}", held.Json, held.Version)
            {
                MailAccounts = [.. this.accounts.Where(account => this.assignments[account.Id].Contains(user))],
            }
            : null;

    /// <summary>Reads the version every held user's record stands at.</summary>
    /// <returns>One version per user.</returns>
    internal IReadOnlyList<UserSettingsDocumentVersion> Versions() =>
        [.. this.users.Select(held => new UserSettingsDocumentVersion(held.Key, held.Value.Version))];

    /// <inheritdoc />
    public Task<IReadOnlyList<MailAccountSummary>> ReadAllAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MailAccountSummary>>(
        [
            .. this.accounts.Take(limit).Select(account => new MailAccountSummary(
                account.Id,
                account.EmailAddress,
                account.DisplayName,
                account.Version,
                [.. this.assignments[account.Id]])),
        ]);

    /// <inheritdoc />
    public Task<MailAccountHolding?> ReadAsync(Guid accountId, CancellationToken cancellationToken) =>
        Task.FromResult(this.Find(accountId) is { } account ? this.HoldingOf(account) : null);

    /// <inheritdoc />
    public Task<MailAccountWrite> CreateAsync(
        UserId user,
        long expectedUserVersion,
        MailAccountRecord account,
        CancellationToken cancellationToken)
    {
        if (!this.users.TryGetValue(user, out var held))
        {
            return Written(MailAccountWriteResult.NotFound, 0);
        }

        if (held.Version != expectedUserVersion)
        {
            return Written(MailAccountWriteResult.VersionSuperseded, held.Version);
        }

        if (this.HoldsAddress(account.EmailAddress, exceptAccount: null))
        {
            return Written(MailAccountWriteResult.AddressHeld, held.Version);
        }

        this.accounts.Add(account with { Version = 1 });
        this.assignments[account.Id] = [user];
        this.MoveVersionOf(user);

        return Written(MailAccountWriteResult.Committed, 1);
    }

    /// <inheritdoc />
    public Task<MailAccountWrite> SaveAsync(MailAccountRecord account, CancellationToken cancellationToken)
    {
        if (this.Find(account.Id) is not { } standing)
        {
            return Written(MailAccountWriteResult.NotFound, 0);
        }

        if (standing.Version != account.Version)
        {
            return Written(MailAccountWriteResult.VersionSuperseded, standing.Version);
        }

        if (this.HoldsAddress(account.EmailAddress, exceptAccount: account.Id))
        {
            return Written(MailAccountWriteResult.AddressHeld, standing.Version);
        }

        var saved = account with { Version = standing.Version + 1 };

        this.accounts[this.accounts.IndexOf(standing)] = saved;
        this.assignments[account.Id].ForEach(this.MoveVersionOf);

        return Written(MailAccountWriteResult.Committed, saved.Version);
    }

    /// <inheritdoc />
    public Task<MailAccountWrite> AssignAsync(
        Guid accountId,
        UserId user,
        long expectedUserVersion,
        CancellationToken cancellationToken)
    {
        if (this.Find(accountId) is not { } account || !this.users.TryGetValue(user, out var held))
        {
            return Written(MailAccountWriteResult.NotFound, 0);
        }

        if (this.assignments[accountId].Contains(user))
        {
            return Written(MailAccountWriteResult.NothingToChange, account.Version);
        }

        if (held.Version != expectedUserVersion)
        {
            return Written(MailAccountWriteResult.VersionSuperseded, held.Version);
        }

        this.assignments[accountId].Add(user);
        this.MoveVersionOf(user);

        return Written(MailAccountWriteResult.Committed, account.Version);
    }

    /// <inheritdoc />
    public Task<MailAccountUnassignment> UnassignAsync(Guid accountId, UserId user, CancellationToken cancellationToken)
    {
        if (!this.assignments.TryGetValue(accountId, out var assigned) || !assigned.Remove(user))
        {
            return Task.FromResult(new MailAccountUnassignment(Unassigned: false, AccountErased: false));
        }

        this.MoveVersionOf(user);

        if (assigned.Count > 0)
        {
            return Task.FromResult(new MailAccountUnassignment(Unassigned: true, AccountErased: false));
        }

        this.Remove(accountId);

        return Task.FromResult(new MailAccountUnassignment(Unassigned: true, AccountErased: true));
    }

    /// <inheritdoc />
    public Task<bool> EraseAsync(Guid accountId, CancellationToken cancellationToken)
    {
        if (!this.assignments.TryGetValue(accountId, out var assigned))
        {
            return Task.FromResult(false);
        }

        assigned.ForEach(this.MoveVersionOf);
        this.Remove(accountId);

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Guid>> ReadSolelyAssignedAsync(UserId user, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(
        [
            .. this.assignments
                .Where(assignment => assignment.Value is [var only] && only == user)
                .Select(static assignment => assignment.Key)
                .Order(),
        ]);

    private static Task<MailAccountWrite> Written(MailAccountWriteResult result, long version) =>
        Task.FromResult(new MailAccountWrite(result, version));

    private MailAccountRecord? Find(Guid accountId) => this.accounts.FirstOrDefault(account => account.Id == accountId);

    private MailAccountHolding HoldingOf(MailAccountRecord account) => new(account, [.. this.assignments[account.Id]]);

    private bool HoldsAddress(string? emailAddress, Guid? exceptAccount) =>
        MailAccountRecord.NormalizedFormOf(emailAddress) is { } normalized
        && this.accounts.Any(account =>
            account.Id != exceptAccount
            && MailAccountRecord.NormalizedFormOf(account.EmailAddress) == normalized);

    private void MoveVersionOf(UserId user) =>
        this.users[user] = this.users[user] with { Version = this.users[user].Version + 1 };

    private void Remove(Guid accountId)
    {
        this.accounts.RemoveAll(account => account.Id == accountId);
        this.assignments.Remove(accountId);
    }

    private sealed record HeldUser(string Json, long Version);
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Runtime.CompilerServices;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Mail;
using Microsoft.Extensions.Primitives;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Serves the accounts a settings snapshot carries, as the persisted source serves the accounts the records hold.</summary>
/// <remarks>
/// <para>
/// A test states its accounts on a snapshot rather than as rows, so this reads them from there: every account the
/// snapshot declares is supervised, a run reads the snapshot itself whenever it still declares the account, and the
/// snapshot's reload token is what announces a change.
/// </para>
/// <para>
/// An account's version is the identity of its declaration. A test changing one account replaces that declaration and
/// leaves the others as the same objects, which is exactly what moves one record's version and no other's.
/// </para>
/// </remarks>
internal sealed class SnapshotMailSynchronizationAccounts(ISettingsSnapshot<MailSynchronizationOptions> settings)
    : IMailSynchronizationAccountSource
{
    private readonly ConditionalWeakTable<MailSynchronizationAccountOptions, StrongBox<long>> versions = new();
    private long issuedVersions;

    /// <summary>Gets or sets what every read of a run's settings fails with, or nothing to let it read.</summary>
    internal Exception? ReadFailure { get; set; }

    /// <inheritdoc />
    public async IAsyncEnumerable<SupervisedMailAccount> ReadSupervisedAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        foreach (var declared in settings.Current.DeclaredAccounts
            .Where(static declared => MailSynchronizationOptions.TryReadAccountId(declared.AccountId) is not null)
            .DistinctBy(static declared => MailSynchronizationOptions.TryReadAccountId(declared.AccountId), StringComparer.Ordinal)
            .ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return new SupervisedMailAccount(MailAccountId.Create(declared.AccountId), this.VersionOf(declared));
        }
    }

    /// <inheritdoc />
    public Task<MailSynchronizationOptions?> ReadRunSettingsAsync(
        MailAccountId account,
        MailSynchronizationOptions? previous,
        CancellationToken cancellationToken)
    {
        if (this.ReadFailure is { } failure)
        {
            return Task.FromException<MailSynchronizationOptions?>(failure);
        }

        var current = settings.Current;

        return Task.FromResult(current.FindConfiguredAccount(account) is null ? null : current);
    }

    /// <inheritdoc />
    /// <remarks>The snapshot itself, whoever is asked about: a test composing it states exactly the users it serves.</remarks>
    public Task<MailSynchronizationOptions> ReadUserSettingsAsync(UserId user, CancellationToken cancellationToken) =>
        Task.FromResult(settings.Current);

    /// <inheritdoc />
    public IChangeToken GetChangeToken() => settings.GetReloadToken();

    private long VersionOf(MailSynchronizationAccountOptions declared) =>
        this.versions.GetValue(declared, _ => new StrongBox<long>(Interlocked.Increment(ref this.issuedVersions))).Value;
}

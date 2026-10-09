// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Mail;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Holds the one mail synchronization snapshot a work unit runs against.</summary>
/// <remarks>
/// <para>
/// A scope reading the published snapshot itself would still be reading it at a moment of its own choosing. A
/// synchronization run schedules its accounts and folders from the snapshot it took when the run began, and each
/// folder then opens a scope of its own; without this, a reload landing between the two would let a folder scheduled
/// from the old account list connect with the new list's endpoint, policy, limits, and credentials — or fail because
/// the account no longer exists. The enclosing operation therefore hands its snapshot down rather than letting the
/// scope re-read one.
/// </para>
/// <para>
/// No snapshot holds every account. The published one carries the deployment's own section and no account at all, so a
/// scope that reads an account's settings is prepared before anything in it reads them: with the accounts of the user a
/// request acts for, or with one account and the other accounts of the users it is assigned to. Both are read
/// asynchronously, which is why preparing is a step of its own rather than something <see cref="Current" /> could do
/// when first asked. A scope nothing prepared finds no account, which fails as an account that is not served rather
/// than answering about somebody else's.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this scoped holder.")]
internal sealed class ScopedMailSynchronizationSettings(
    ISettingsSnapshot<MailSynchronizationOptions> publishedSettings,
    IMailSynchronizationAccountSource accounts) : IMailAccountSettingsScope
{
    private MailSynchronizationOptions? snapshot;

    /// <summary>Gets the snapshot this scope runs against, taking the published one when nothing prepared the scope.</summary>
    internal MailSynchronizationOptions Current => this.snapshot ??= publishedSettings.Current;

    /// <summary>Hands the enclosing operation's snapshot to this scope.</summary>
    /// <param name="runSnapshot">The snapshot the enclosing run captured when it began.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="runSnapshot" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the scope has already answered with a different snapshot.</exception>
    /// <remarks>Call this before anything in the scope reads settings; a scope that has already answered cannot change its mind without making the two answers inconsistent.</remarks>
    internal void UseRunSnapshot(MailSynchronizationOptions runSnapshot)
    {
        ArgumentNullException.ThrowIfNull(runSnapshot);

        if (this.snapshot is not null && !ReferenceEquals(this.snapshot, runSnapshot))
        {
            throw new InvalidOperationException(
                "The scope already resolved mail synchronization settings, so the enclosing run's snapshot would contradict what it has already been given.");
        }

        this.snapshot = runSnapshot;
    }

    /// <summary>Prepares this scope with the accounts of the user it acts for.</summary>
    /// <param name="user">The user the scope's work is for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes once the scope holds the user's accounts, or none where the user is not served.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the scope has already answered with a different snapshot.</exception>
    internal async Task UseUserSettingsAsync(UserId user, CancellationToken cancellationToken) =>
        this.UseRunSnapshot(await accounts.ReadUserSettingsAsync(user, cancellationToken));

    /// <inheritdoc />
    /// <remarks>The scope holds the account beside the other accounts of every user it is assigned to.</remarks>
    /// <exception cref="InvalidOperationException">Thrown when the scope has already answered with a different snapshot.</exception>
    public async Task<bool> UseAccountSettingsAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        if (await accounts.ReadRunSettingsAsync(account, previous: null, cancellationToken) is not { } settings)
        {
            return false;
        }

        this.UseRunSnapshot(settings);

        return true;
    }
}

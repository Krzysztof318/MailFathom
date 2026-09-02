// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Mail.Mutations.Audit;

/// <summary>Ages one account's audit trail out at the window that account configured.</summary>
/// <remarks>
/// <para>
/// A trail that is never pruned is a store of derived personal data growing without an end anybody undertook to hold it
/// to, which is the storage-limitation half of what enabling the trail commits an operator to. The window is the
/// account's own, so a deployment holding one mailbox for accountability and another for convenience answers for each
/// separately.
/// </para>
/// <para>
/// It runs on the account's own synchronization run rather than on a worker of its own, for the reason convergence does:
/// an account already has a loop that comes round, and a second schedule would be a second thing to configure, watch,
/// and reason about for work that is one bounded delete.
/// </para>
/// <para>
/// The window is read from the account's current configuration rather than from the entries. Retention is a decision
/// about what the deployment is willing to keep now, unlike the enablement an in-flight mutation carries with it, and an
/// operator who shortens it means the entries already written as much as the ones still to come.
/// </para>
/// </remarks>
public sealed class MailboxMutationAuditTrailRetention
{
    /// <summary>The greatest number of entries one pass erases.</summary>
    /// <remarks>
    /// It is a constant rather than a setting because nothing an operator would tune depends on it: a pass that reaches
    /// the bound is followed by another on the account's next run, so the number decides how long a backlog takes to
    /// clear rather than what is kept. What it exists for is the one case that produces a backlog at all — an operator
    /// shortening a long window — where an unbounded delete would lock the trail against every append behind it.
    /// </remarks>
    public const int MaximumEntriesErasedPerPass = 5_000;

    private readonly IMailboxMutationAuditSettingsReader settingsReader;
    private readonly IMailboxMutationAuditEntryStore store;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the retention pass from the account's settings and the trail it erases from.</summary>
    /// <param name="settingsReader">Supplies the window one account keeps its entries for.</param>
    /// <param name="store">Holds the trail the pass erases from.</param>
    /// <param name="timeProvider">Measures the window back from now.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public MailboxMutationAuditTrailRetention(
        IMailboxMutationAuditSettingsReader settingsReader,
        IMailboxMutationAuditEntryStore store,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(settingsReader);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.settingsReader = settingsReader;
        this.store = store;
        this.timeProvider = timeProvider;
    }

    /// <summary>Erases everything in one account's trail that has outlived the window it configured.</summary>
    /// <param name="account">The account whose trail is aged.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>How many entries were erased.</returns>
    /// <remarks>
    /// It runs whether or not the trail is currently on, because an account that has just been switched off still holds
    /// the entries written while it was on and those are what the window was configured for. A window of zero or less
    /// names no boundary at all and erases nothing, which is what an account this deployment no longer configures
    /// reports.
    /// </remarks>
    public Task<int> EraseExpiredAsync(MailAccountIdentity account, CancellationToken cancellationToken)
    {
        var retention = this.settingsReader.GetAuditSettings(account.Id).Retention;

        if (retention <= TimeSpan.Zero)
        {
            return Task.FromResult(0);
        }

        return this.store.EraseCompletedBeforeAsync(
            account,
            this.timeProvider.GetUtcNow() - retention,
            MaximumEntriesErasedPerPass,
            cancellationToken);
    }
}

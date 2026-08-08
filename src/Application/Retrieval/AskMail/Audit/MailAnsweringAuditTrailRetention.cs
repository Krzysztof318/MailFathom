// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Retrieval.AskMail.Audit;

/// <summary>Ages one account's answering record out at the window that account configured.</summary>
/// <remarks>
/// <para>
/// A record that is never pruned is a store of derived personal data growing without an end anybody undertook to hold it
/// to, which is the storage-limitation half of what enabling the record commits an operator to. The window is the
/// account's own, so a deployment holding one mailbox for accountability and another for convenience answers for each
/// separately.
/// </para>
/// <para>
/// It runs on the account's own synchronization run rather than on a worker of its own, for the reason the mutation
/// trail's retention does: an account already has a loop that comes round, and a second schedule would be a second thing
/// to configure, watch, and reason about for work that is one bounded delete.
/// </para>
/// <para>
/// The window is read from the account's current configuration rather than from the entries. Retention is a decision
/// about what the deployment is willing to keep now, and an operator who shortens it means the entries already written
/// as much as the ones still to come.
/// </para>
/// </remarks>
public sealed class MailAnsweringAuditTrailRetention
{
    /// <summary>The greatest number of entries one pass erases.</summary>
    /// <remarks>
    /// It is a constant rather than a setting because nothing an operator would tune depends on it: a pass that reaches
    /// the bound is followed by another on the account's next run, so the number decides how long a backlog takes to
    /// clear rather than what is kept. It is smaller than the mutation trail's bound because erasing an entry erases the
    /// emails it named with it, so one row here is several.
    /// </remarks>
    public const int MaximumEntriesErasedPerPass = 1_000;

    private readonly IMailAnsweringAuditSettingsReader settingsReader;
    private readonly IMailAnsweringAuditEntryStore store;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the retention pass from the account's settings and the record it erases from.</summary>
    /// <param name="settingsReader">Supplies the window one account keeps its entries for.</param>
    /// <param name="store">Holds the record the pass erases from.</param>
    /// <param name="timeProvider">Measures the window back from now.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public MailAnsweringAuditTrailRetention(
        IMailAnsweringAuditSettingsReader settingsReader,
        IMailAnsweringAuditEntryStore store,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(settingsReader);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.settingsReader = settingsReader;
        this.store = store;
        this.timeProvider = timeProvider;
    }

    /// <summary>Erases everything in one account's record that has outlived the window it configured.</summary>
    /// <param name="accountId">The account whose record is aged.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>How many entries were erased.</returns>
    /// <remarks>
    /// It runs whether or not the record is currently on, because an account that has just been switched off still holds
    /// the entries written while it was on and those are what the window was configured for. A window of zero or less
    /// names no boundary at all and erases nothing, which is what an account this deployment no longer configures
    /// reports.
    /// </remarks>
    public Task<int> EraseExpiredAsync(MailAccountId accountId, CancellationToken cancellationToken)
    {
        var retention = this.settingsReader.GetAnsweringAuditSettings(accountId).Retention;

        if (retention <= TimeSpan.Zero)
        {
            return Task.FromResult(0);
        }

        return this.store.EraseCompletedBeforeAsync(
            accountId,
            this.timeProvider.GetUtcNow() - retention,
            MaximumEntriesErasedPerPass,
            cancellationToken);
    }
}

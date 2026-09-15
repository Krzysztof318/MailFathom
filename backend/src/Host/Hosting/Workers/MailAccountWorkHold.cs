// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>The supervision scopes one quiescing is holding, given back together however it ends.</summary>
/// <remarks>
/// <para>
/// A set rather than one hold because an erasure disposes of every account its user was the last assigned, and every
/// one of them has to be stopped before any of them is deleted. The accounts taken before one that would not fall
/// quiet are held by this replica and would stay unsupervised until their leases expired if nothing released them,
/// which is why a refusal disposes of this exactly as a completed run does.
/// </para>
/// <para>
/// Each hold is renewed for as long as this one is kept, because what runs under it is a transaction rather than an
/// instant: a lease left to stand on its duration alone would expire under a long erasure and let another replica
/// start supervising an account being deleted, which is the race the hold was taken to close.
/// </para>
/// </remarks>
internal sealed class MailAccountWorkHold(TimeProvider timeProvider) : IAsyncDisposable
{
    /// <summary>How long the renewals are given to stop before the holds are given back regardless.</summary>
    /// <remarks>
    /// A renewal loop ends as soon as it observes the stop, so this bounds a database that has stopped answering rather
    /// than the ordinary path. Giving the holds back late is better than not at all, and an expiry covers whatever is
    /// left.
    /// </remarks>
    private static readonly TimeSpan RenewalStopTimeout = TimeSpan.FromSeconds(5);

    private readonly List<(MailAccountId Account, WorkLeaseHold Hold)> holds = [];
    private readonly List<Task> renewals = [];
    private readonly CancellationTokenSource renewalStop = new();
    private bool released;

    /// <summary>Gets the token of every hold, each cancelled on the first renewal that did not complete.</summary>
    /// <remarks>
    /// What the work under this hold is run against. A hold lost part-way is an account another replica may take, so
    /// work that went on deleting under it would be the second writer the hold was taken to exclude — and a deletion
    /// cancelled mid-transaction rolls back, which is the answer that leaves the deployment where it was.
    /// </remarks>
    internal IReadOnlyList<CancellationToken> Lost => [.. this.holds.Select(static held => held.Hold.Lost)];

    /// <summary>Gets the first account whose hold was lost, or <see langword="null" /> while every one is still held.</summary>
    /// <remarks>
    /// The caller is told which mailbox would not stay still rather than that something was cancelled, which is the
    /// difference between an answer an operator can act on and a fault they can only retry.
    /// </remarks>
    internal MailAccountId? LostAccount => this.holds
        .Where(static held => held.Hold.Lost.IsCancellationRequested)
        .Select(static held => (MailAccountId?)held.Account)
        .FirstOrDefault();

    /// <summary>Takes one more account's hold into this one, and starts renewing it.</summary>
    /// <param name="account">The account the hold is on, so a hold lost later can be named.</param>
    /// <param name="hold">The hold on that account's supervision scope.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="hold" /> is <see langword="null" />.</exception>
    internal void Keep(MailAccountId account, WorkLeaseHold hold)
    {
        ArgumentNullException.ThrowIfNull(hold);

        this.holds.Add((account, hold));
        this.renewals.Add(hold.KeepAsync(this.renewalStop.Token));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Every hold is given back even where one release fails, because a scope left held is an account nothing
    /// supervises until the lease expires. A release that cannot be written is the hold's own to report and is covered
    /// by that expiry.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (this.released)
        {
            return;
        }

        this.released = true;

        await this.renewalStop.CancelAsync();

        try
        {
            await Task.WhenAll(this.renewals).WaitAsync(RenewalStopTimeout, timeProvider);
        }
        catch (TimeoutException)
        {
            // The holds are given back below regardless, and whatever renewal is still in flight ends against a scope
            // this replica no longer holds, which writes nothing.
        }

        foreach (var (_, hold) in this.holds)
        {
            await hold.ReleaseAsync();
            hold.Dispose();
        }

        this.holds.Clear();
        this.renewals.Clear();
        this.renewalStop.Dispose();
    }
}

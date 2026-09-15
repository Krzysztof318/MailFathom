// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Jobs;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Mail;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Stops every replica writing to a set of mail accounts, and holds them stopped while something disposes of them.</summary>
/// <remarks>
/// <para>
/// It exists for erasure and for nothing else. A deletion narrowed on a mail account is outside every lock the row it
/// was asked about could take: a synchronization run and a job handler write rows keyed to the account rather than to
/// the person, so a walk that deleted everything would still be racing whatever was mid-write when it ran. What closes
/// that is stopping the writers first, which is this.
/// </para>
/// <para>
/// Synchronization is stopped by taking the account's own supervision scope, which is the same lease
/// <see cref="MailSynchronizationCoordinator" /> takes before it starts a supervisor. Holding it means no replica is
/// running that account and none will start it while the hold is kept — the exclusion is the lease table's rather than
/// this type's, so it reaches every replica and not only the one the request arrived at. The replica that was holding
/// it lets go once the account leaves the set it serves, which is what the caller arranges before it asks here.
/// </para>
/// <para>
/// Jobs are not leased per account, so there is nothing to hold: what is waited for instead is that no live claim holds
/// a job naming one of these accounts. New ones stop arriving because the synchronization runs that enqueue them are
/// stopped by the paragraph above, and the queued rows go with the account in the erasure itself.
/// </para>
/// <para>
/// Every wait is bounded, and running out is a refusal rather than a delay: an erasure that went ahead over work still
/// in flight is the failure this exists to prevent, and half an erasure is worse than none. The bound is fixed rather
/// than configured because it bounds an operator waiting at a request rather than how long a run may take — a
/// deployment whose runs regularly outlast it asks again rather than waits longer.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this service.")]
internal sealed class MailAccountWorkQuiesce(
    IServiceScopeFactory scopeFactory,
    ISettingsSnapshot<MailSynchronizationOptions> settings,
    ILoggerFactory loggerFactory,
    TimeProvider timeProvider,
    TimeSpan? quiesceTimeout = null) : IMailAccountWorkQuiescing
{
    /// <summary>How long the accounts are given to fall quiet before the caller is told what is still running.</summary>
    internal static readonly TimeSpan QuiesceTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long is left between asking again, so a wait costs a bounded number of statements rather than a spin.</summary>
    private static readonly TimeSpan AskAgainAfter = TimeSpan.FromMilliseconds(500);

    /// <summary>The bound this instance applies, which is <see cref="QuiesceTimeout" /> everywhere but a suite.</summary>
    /// <remarks>
    /// A parameter rather than a setting: nothing an operator writes reaches it, and the composition root passes
    /// nothing, so a deployment has one bound. What it is for is a test stating the bound it is asserting against
    /// instead of waiting one out, which is the only reason the value is reachable from outside at all.
    /// </remarks>
    private readonly TimeSpan bound = quiesceTimeout ?? QuiesceTimeout;

    /// <inheritdoc />
    public async Task<string?> RunQuiescedAsync(
        IReadOnlyList<MailAccountId> accounts,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(work);

        var snapshot = settings.Current;
        var waitingUntil = timeProvider.GetUtcNow() + this.bound;
        await using var quiesced = new MailAccountWorkHold(timeProvider);

        foreach (var account in accounts)
        {
            if (await this.TakeSupervisionAsync(account, snapshot, waitingUntil, cancellationToken) is not { } hold)
            {
                return $"Mail account {account.Value} is still being synchronized, so erasing it now would leave rows behind that no deletion here could reach. Nothing was erased; ask again once the run has ended.";
            }

            quiesced.Keep(hold);
        }

        if (await this.WaitForJobsToFinishAsync(accounts, waitingUntil, cancellationToken) is { } running)
        {
            return $"A job is still running for mail account {running}, so erasing it now would leave rows behind that no deletion here could reach. Nothing was erased; ask again once the job has finished.";
        }

        // The work runs against the holds rather than only against the caller, so a lease this replica stops being able
        // to show it has cancels the deletion instead of letting it commit as a second writer.
        using var held = CancellationTokenSource.CreateLinkedTokenSource([cancellationToken, .. quiesced.Lost]);

        await work(held.Token);

        return null;
    }

    /// <summary>Takes one account's supervision scope, asking again until it is free or the bound has passed.</summary>
    /// <remarks>
    /// A refused claim is an account another replica is supervising, which is the ordinary answer for as long as that
    /// replica is still draining the run it was in. Asking again is therefore the wait, and the bound is what turns a
    /// replica that never lets go into an answer rather than a request that never returns.
    /// </remarks>
    private async Task<WorkLeaseHold?> TakeSupervisionAsync(
        MailAccountId account,
        MailSynchronizationOptions snapshot,
        DateTimeOffset waitingUntil,
        CancellationToken cancellationToken)
    {
        var scope = MailAccountSupervisionScope.For(account);

        while (true)
        {
            var hold = await WorkLeaseHold.TryTakeAsync(
                scope,
                snapshot.LeaseDuration,
                snapshot.LeaseRenewalInterval,
                scopeFactory,
                loggerFactory.CreateLogger<WorkLeaseHold>(),
                timeProvider,
                cancellationToken);

            if (hold is not null || timeProvider.GetUtcNow() >= waitingUntil)
            {
                return hold;
            }

            await Task.Delay(AskAgainAfter, timeProvider, cancellationToken);
        }
    }

    /// <summary>Waits until no live claim holds a job for any of these accounts, and names the first that outlasts the bound.</summary>
    private async Task<string?> WaitForJobsToFinishAsync(
        IReadOnlyList<MailAccountId> accounts,
        DateTimeOffset waitingUntil,
        CancellationToken cancellationToken)
    {
        var accountIds = accounts.Select(static account => account.Value).ToArray();

        while (true)
        {
            await using var serviceScope = scopeFactory.CreateAsyncScope();

            var running = await serviceScope.ServiceProvider
                .GetRequiredService<IJobStore>()
                .ReadAccountsWithWorkInFlightAsync(accountIds, cancellationToken);

            if (running.Count == 0)
            {
                return null;
            }

            if (timeProvider.GetUtcNow() >= waitingUntil)
            {
                return running[0];
            }

            await Task.Delay(AskAgainAfter, timeProvider, cancellationToken);
        }
    }
}

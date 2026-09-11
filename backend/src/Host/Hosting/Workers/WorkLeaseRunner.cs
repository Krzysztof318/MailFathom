// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Runs application work under a <see cref="WorkLeaseHold" /> taken, renewed, and given back on one pair of timings.</summary>
/// <remarks>
/// The hold is given back only after the work has ended, and before the caller's next step, so work that hands the rest
/// of itself to a successor leaves the scope free for that successor to take at once.
/// </remarks>
internal sealed class WorkLeaseRunner : IWorkLeaseRunner
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILoggerFactory loggerFactory;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan leaseDuration;
    private readonly TimeSpan renewalInterval;

    /// <summary>Initializes the runner from the timings every hold it takes is held on.</summary>
    /// <param name="scopeFactory">Creates the scope each statement against the lease store runs in.</param>
    /// <param name="loggerFactory">Supplies the logger each hold records a lost lease under.</param>
    /// <param name="timeProvider">Schedules the renewals and bounds how long each may take.</param>
    /// <param name="leaseDuration">How long a scope is held from each claim or renewal.</param>
    /// <param name="renewalInterval">How long after the last confirmation the next renewal is sent; shorter than <paramref name="leaseDuration" />.</param>
    public WorkLeaseRunner(
        IServiceScopeFactory scopeFactory,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider,
        TimeSpan leaseDuration,
        TimeSpan renewalInterval)
    {
        this.scopeFactory = scopeFactory;
        this.loggerFactory = loggerFactory;
        this.timeProvider = timeProvider;
        this.leaseDuration = leaseDuration;
        this.renewalInterval = renewalInterval;
    }

    /// <inheritdoc />
    public async Task<bool> TryRunUnderLeaseAsync(
        WorkScope scope,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        using var hold = await WorkLeaseHold.TryTakeAsync(
            scope,
            this.leaseDuration,
            this.renewalInterval,
            this.scopeFactory,
            this.loggerFactory.CreateLogger<WorkLeaseHold>(),
            this.timeProvider,
            cancellationToken);

        if (hold is null)
        {
            return false;
        }

        using var renewalStop = new CancellationTokenSource();
        using var heldWork = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, hold.Lost);

        var renewals = hold.KeepAsync(renewalStop.Token);

        try
        {
            await work(heldWork.Token);
        }
        finally
        {
            await renewalStop.CancelAsync();
            await renewals;
            await hold.ReleaseAsync();
        }

        return true;
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Coordination;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>This replica's hold on one unit of work that must not run twice: taken once, kept by renewal, and given back.</summary>
/// <remarks>
/// <para>
/// The work a hold guards is cancelled through <see cref="Lost" />, and the only thing that cancels it is a renewal that
/// did not complete — refused because another replica took the scope, failed, or not answered in time. That is the
/// ordering <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md">ADR 0031</see>
/// settles: a holder stops on the first renewal it fails to complete rather than when the lease's clock says it expired.
/// </para>
/// <para>
/// The margin between the renewal interval and the lease duration is split in two. A renewal is given the first half to
/// be answered in, so a database that stalls is noticed strictly before the lease could expire, and the second half is
/// what the cancelled work has to tear its connections down in before another replica may take the scope. Every instant
/// here is measured from the moment the last successful claim or renewal was <em>sent</em>, because the expiry the
/// database stamped is at least that far out and this process's clock is never compared with the database's.
/// </para>
/// <para>
/// What it promises is the lease's own: one writer, never one runner. Cancelling the work reaches this process and
/// nothing a mail server has already accepted, which is why the teardown half of the margin exists at all.
/// </para>
/// </remarks>
internal sealed partial class WorkLeaseHold : IDisposable
{
    /// <summary>How long giving a hold back may take before it is left to expire instead.</summary>
    /// <remarks>
    /// A release is an optimization of the ordinary case, and at shutdown it runs after the host's own stopping token has
    /// fired, so it is bounded here rather than by a caller. It fits inside the five seconds the host keeps beyond the
    /// synchronization drain for the services that stop beside it; a release that cannot finish in that time is one the
    /// expiry covers.
    /// </remarks>
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource lost = new();
    private readonly WorkScope scope;
    private readonly WorkLeaseHolder holder;
    private readonly TimeSpan leaseDuration;
    private readonly TimeSpan renewalInterval;
    private readonly TimeSpan renewalAnsweredWithin;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<WorkLeaseHold> logger;
    private readonly TimeProvider timeProvider;
    private long confirmedAt;

    private WorkLeaseHold(
        WorkScope scope,
        WorkLeaseHolder holder,
        TimeSpan leaseDuration,
        TimeSpan renewalInterval,
        long confirmedAt,
        IServiceScopeFactory scopeFactory,
        ILogger<WorkLeaseHold> logger,
        TimeProvider timeProvider)
    {
        this.scope = scope;
        this.holder = holder;
        this.leaseDuration = leaseDuration;
        this.renewalInterval = renewalInterval;
        this.renewalAnsweredWithin = renewalInterval + ((leaseDuration - renewalInterval) / 2);
        this.confirmedAt = confirmedAt;
        this.scopeFactory = scopeFactory;
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    /// <summary>Gets a token cancelled the moment this replica can no longer show it holds the scope.</summary>
    /// <remarks>Link the guarded work's own tokens to it, so a hold that is lost stops the work rather than its next pass.</remarks>
    internal CancellationToken Lost => this.lost.Token;

    /// <summary>Asks for a scope once, and hands back a hold when it was granted.</summary>
    /// <param name="scope">The unit of work to hold.</param>
    /// <param name="leaseDuration">How long the scope is held from each claim or renewal.</param>
    /// <param name="renewalInterval">How long after the last confirmation the next renewal is sent; shorter than <paramref name="leaseDuration" />.</param>
    /// <param name="scopeFactory">Creates the scope each statement against the lease store runs in.</param>
    /// <param name="logger">Records a hold lost, a claim or a release that failed, and never anything from a message.</param>
    /// <param name="timeProvider">Schedules the renewals and bounds how long each may take.</param>
    /// <param name="cancellationToken">Cancels the claim.</param>
    /// <returns>The hold, or <see langword="null" /> when another replica holds the scope or the claim could not be made.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="renewalInterval" /> is not shorter than <paramref name="leaseDuration" />.</exception>
    /// <remarks>
    /// Neither answer that takes nothing is a failure to report upward. The work belongs to somebody else for now, or the
    /// database could not be asked, and in both cases the caller asks again on the interval it would have run on.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A claim that could not be made leaves the work unheld, which is what a refused claim does too; the caller asks again on its own interval.")]
    internal static async Task<WorkLeaseHold?> TryTakeAsync(
        WorkScope scope,
        TimeSpan leaseDuration,
        TimeSpan renewalInterval,
        IServiceScopeFactory scopeFactory,
        ILogger<WorkLeaseHold> logger,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(renewalInterval, leaseDuration);

        var holder = WorkLeaseHolder.NewHold();
        var askedAt = timeProvider.GetTimestamp();

        try
        {
            await using var serviceScope = scopeFactory.CreateAsyncScope();

            var lease = await serviceScope.ServiceProvider
                .GetRequiredService<IWorkLeaseStore>()
                .ClaimAsync(scope, holder, leaseDuration, cancellationToken);

            return lease is null
                ? null
                : new WorkLeaseHold(
                    scope,
                    holder,
                    leaseDuration,
                    renewalInterval,
                    askedAt,
                    scopeFactory,
                    logger,
                    timeProvider);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogClaimFailed(logger, exception, scope.Value);

            return null;
        }
    }

    /// <summary>Renews the hold on its interval until told to stop, and cancels <see cref="Lost" /> on the first renewal that does not complete.</summary>
    /// <param name="stopToken">Stops renewing; cancelled once the guarded work has ended and the hold is about to be given back.</param>
    /// <returns>A task that completes when renewing stops or the hold is lost, and that never faults.</returns>
    internal async Task KeepAsync(CancellationToken stopToken)
    {
        try
        {
            while (true)
            {
                var dueIn = this.renewalInterval - this.timeProvider.GetElapsedTime(this.confirmedAt);

                if (dueIn > TimeSpan.Zero)
                {
                    await Task.Delay(dueIn, this.timeProvider, stopToken);
                }

                if (!await this.TryRenewAsync(stopToken))
                {
                    await this.lost.CancelAsync();

                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            // The guarded work ended. Giving the hold back is the caller's next step.
        }
    }

    /// <summary>Gives the scope back, so another replica can take it without waiting out the expiry.</summary>
    /// <returns>A task that completes once the release was written or given up on, and that never faults.</returns>
    /// <remarks>
    /// Written whether or not the hold was lost. The release is conditional on this hold still being the holder, so after a
    /// takeover it frees nothing, and after a renewal that merely went unanswered it frees a scope this replica may in fact
    /// still have held — which is the case where another replica is waiting on the expiry for nothing.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A release that failed leaves the scope to expire, which is what a crash does; nothing may be built on a release having happened.")]
    internal async Task ReleaseAsync()
    {
        using var releaseDue = new CancellationTokenSource(ReleaseTimeout, this.timeProvider);

        try
        {
            await using var serviceScope = this.scopeFactory.CreateAsyncScope();

            await serviceScope.ServiceProvider
                .GetRequiredService<IWorkLeaseStore>()
                .ReleaseAsync(this.scope, this.holder, releaseDue.Token);
        }
        catch (Exception exception)
        {
            this.LogReleaseFailed(exception, this.scope.Value);
        }
    }

    /// <inheritdoc />
    public void Dispose() => this.lost.Dispose();

    /// <summary>Sends one renewal and reports whether this replica still holds the scope once it was answered.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A renewal that failed is a hold this replica can no longer show it has, so the guarded work stops; the reason is logged here.")]
    private async Task<bool> TryRenewAsync(CancellationToken stopToken)
    {
        var sentAt = this.timeProvider.GetTimestamp();
        var answerDueIn = this.renewalAnsweredWithin - this.timeProvider.GetElapsedTime(this.confirmedAt);

        // A process that was paused past the point a renewal had to be answered by cannot know whether its lease is still
        // its own, so it does not ask: the work stops before anything else it does could be a second writer's.
        if (answerDueIn <= TimeSpan.Zero)
        {
            this.LogRenewalNotAnswered(this.scope.Value);

            return false;
        }

        using var answerDue = new CancellationTokenSource(answerDueIn, this.timeProvider);
        using var renewal = CancellationTokenSource.CreateLinkedTokenSource(answerDue.Token, stopToken);

        try
        {
            await using var serviceScope = this.scopeFactory.CreateAsyncScope();

            var renewed = await serviceScope.ServiceProvider
                .GetRequiredService<IWorkLeaseStore>()
                .RenewAsync(this.scope, this.holder, this.leaseDuration, renewal.Token);

            if (renewed is null)
            {
                this.LogHoldTakenOver(this.scope.Value);

                return false;
            }

            this.confirmedAt = sentAt;

            return true;
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (answerDue.IsCancellationRequested)
        {
            this.LogRenewalNotAnswered(this.scope.Value);

            return false;
        }
        catch (Exception exception)
        {
            this.LogRenewalFailed(exception, this.scope.Value);

            return false;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The hold on {WorkScope} could not be asked for; it stays unheld here and is asked for again on the next interval.")]
    private static partial void LogClaimFailed(ILogger logger, Exception exception, string workScope);

    /// <summary>Records the ordinary end of a hold that moved, which is worth a warning because it stopped work in flight.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Another replica took over {WorkScope}, so the work it guarded here was stopped; that replica carries it from what was committed.")]
    private partial void LogHoldTakenOver(string workScope);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The hold on {WorkScope} could not be renewed, so the work it guarded here was stopped before its lease could expire; whichever replica takes it next resumes from what was committed.")]
    private partial void LogRenewalFailed(Exception exception, string workScope);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The hold on {WorkScope} was not renewed in time, so the work it guarded here was stopped before its lease could expire; whichever replica takes it next resumes from what was committed.")]
    private partial void LogRenewalNotAnswered(string workScope);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The hold on {WorkScope} could not be given back; another replica can take it once its lease expires.")]
    private partial void LogReleaseFailed(Exception exception, string workScope);
}

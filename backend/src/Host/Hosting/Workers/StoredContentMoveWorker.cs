// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Coordination;
using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.Persistence;
using MailFathom.Host.Configuration.Persistence;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Carries the move of stored content one bounded pass per interval, on whichever replica holds the move.</summary>
/// <remarks>
/// <para>
/// The interval is half the move's rate and the pass's own ceilings are the other half. Between them the deployment
/// spends most of every interval on the work a mailbox is actually for, which is what "the move yields to ordinary work"
/// means in practice: it never holds the database, the process, or the endpoint for longer than one bounded pass.
/// </para>
/// <para>
/// It runs whether or not a move exists, and does not end itself when one finishes. A move is started, paused, and
/// resumed by an operator at any time, so the worker's tick is the deployment's readiness to carry one — a single-row
/// read when there is nothing to do, which is what a deployment that never moves its content pays.
/// </para>
/// <para>
/// The move is one run for the whole deployment, so a pass is carried only by the replica holding the move's lease, as
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md">ADR 0031</see>
/// decides. The lease is taken for one pass, kept through the interval after it, and given back. Keeping the interval is
/// what makes the rate the deployment's rather than one per replica, because no replica can start the next pass until it
/// has elapsed; giving it back is what lets the move outlive the replica that started it.
/// </para>
/// <para>
/// Registered only where the deployment selected the object backend, because there is nowhere else to carry content to.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class StoredContentMoveWorker : BackgroundService
{
    /// <summary>The lease the move is held under, which is the deployment's as the move's one run is.</summary>
    internal static readonly WorkScope MoveScope = WorkScope.Create("stored-content-move");

    private readonly IServiceScopeFactory scopeFactory;
    private readonly ContentMoveOptions settings;
    private readonly ILogger<StoredContentMoveWorker> logger;
    private readonly ILoggerFactory loggerFactory;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the worker.</summary>
    /// <param name="scopeFactory">Opens the scope the readiness read, one pass, and each lease statement run inside.</param>
    /// <param name="settings">The interval between passes, what one pass may carry, and how the move's lease is held.</param>
    /// <param name="logger">Records what a pass carried, in counts alone.</param>
    /// <param name="loggerFactory">Creates the logger a hold on the move reports a loss, or a claim or release that could not be written, through.</param>
    /// <param name="timeProvider">Drives the interval and the lease renewals.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public StoredContentMoveWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<ContentMoveOptions> settings,
        ILogger<StoredContentMoveWorker> logger,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.scopeFactory = scopeFactory;
        this.settings = settings.Value;
        this.logger = logger;
        this.loggerFactory = loggerFactory;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(this.settings.Interval, this.timeProvider, stoppingToken);

        while (true)
        {
            if (!await this.TryCarryPassWhileHeldAsync(stoppingToken))
            {
                await Task.Delay(this.settings.Interval, this.timeProvider, stoppingToken);
            }
        }
    }

    /// <summary>Carries one pass if this replica takes the move, and keeps the move through the interval after it.</summary>
    /// <returns>Whether this replica held the move, in which case the interval after its pass has already elapsed.</returns>
    /// <remarks>
    /// <para>
    /// The hold is renewed for the pass and the interval alike, and a renewal that does not complete cancels the pass
    /// through its token rather than letting it run on, because another replica may start one as soon as the lease
    /// expires. The pass still records where it got to on the way out.
    /// </para>
    /// <para>
    /// A pass cut short that way cannot leave the move inconsistent, because nothing it writes assumes it was alone. A row
    /// is repointed only while it is still database-backed, so a payload two passes both reached is pointed at and counted
    /// once, and the other object is an orphan the reclamation sweep removes, as
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md">ADR 0017</see>
    /// § 2 accepts. The run's progress is written from a fresh read, and the walk names only what the database still
    /// holds, so whichever replica carries the next pass resumes rather than repeats.
    /// </para>
    /// </remarks>
    private async Task<bool> TryCarryPassWhileHeldAsync(CancellationToken stoppingToken)
    {
        using var hold = await this.TryTakeMoveAsync(stoppingToken);

        if (hold is null)
        {
            return false;
        }

        return await hold.RunWhileHeldAsync(
            async heldPass =>
            {
                await this.RunOnceAsync(heldPass);
                await Task.Delay(this.settings.Interval, this.timeProvider, stoppingToken);

                return true;
            },
            stoppingToken);
    }

    /// <summary>Asks for the move's lease once a move is waiting for a pass, and never before.</summary>
    /// <returns>The hold, or <see langword="null" /> when there is nothing to carry, another replica holds the move, or neither could be asked.</returns>
    private async Task<WorkLeaseHold?> TryTakeMoveAsync(CancellationToken stoppingToken)
    {
        if (!await this.HasMoveToCarryAsync(stoppingToken))
        {
            return null;
        }

        var hold = await WorkLeaseHold.TryTakeAsync(
            MoveScope,
            this.settings.LeaseDuration,
            this.settings.LeaseRenewalInterval,
            this.scopeFactory,
            this.loggerFactory.CreateLogger<WorkLeaseHold>(),
            this.timeProvider,
            stoppingToken);

        if (hold is null)
        {
            LogMoveHeldElsewhere(this.logger);
        }

        return hold;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A readiness read that failed is a pass that could not start; the next interval asks again.")]
    private async Task<bool> HasMoveToCarryAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = this.scopeFactory.CreateAsyncScope();

            return await scope.ServiceProvider
                .GetRequiredService<StoredContentMove>()
                .HasMoveToCarryAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogReadinessReadFailed(this.logger, exception);

            return false;
        }
    }

    /// <summary>Runs one bounded pass, keeping the worker alive whatever the pass made of it.</summary>
    /// <remarks>
    /// A failed pass is not a failed move. The database being briefly unavailable, an endpoint refusing a request, or a
    /// competing writer winning a race says nothing about whether payloads remain, and everything a pass repointed is
    /// durable on its own — so the next interval resumes from the committed position rather than starting over.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The hosted worker isolates an unexpected failure so a later interval can resume from the committed position.")]
    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = this.scopeFactory.CreateAsyncScope();

            var move = scope.ServiceProvider.GetRequiredService<StoredContentMove>();
            var pass = await move.RunAsync(cancellationToken);

            if (pass.CopiedPayloadCount > 0 || pass.FailedPayloadCount > 0)
            {
                LogPassCarried(this.logger, pass.CopiedPayloadCount, pass.MovedByteCount, pass.FailedPayloadCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown or a lost hold rather than a failure: a rolling restart must not read as a move that broke on
            // every replica, and a hold that was lost has already said so itself.
        }
        catch (PersistenceConcurrencyConflictException exception)
        {
            LogPassDeferredAfterConcurrencyConflict(this.logger, exception);
        }
        catch (Exception exception)
        {
            LogPassFailed(this.logger, exception);
        }
    }

    /// <summary>Reports one pass in counts only; no key, identity, or fragment of a message may reach a log.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Moved {CopiedPayloadCount} stored payloads carrying {MovedByteCount} bytes into the object backend, and left {FailedPayloadCount} of them in the database.")]
    private static partial void LogPassCarried(ILogger logger, long copiedPayloadCount, long movedByteCount, long failedPayloadCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Deferred a pass of the stored-content move after an unresolved optimistic concurrency conflict; the next interval will resume from the committed position.")]
    private static partial void LogPassDeferredAfterConcurrencyConflict(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A pass of the stored-content move failed; the next interval will resume from the committed position.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);

    /// <summary>Records a readiness read that failed, which is no pass at all: nothing was reached and nothing was committed.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not read whether a stored-content move is waiting for a pass; the next interval will ask again.")]
    private static partial void LogReadinessReadFailed(ILogger logger, Exception exception);

    /// <summary>Records the ordinary answer for a replica another one is carrying the move for, which is why it is not worth more than debug.</summary>
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "The stored-content move is not carried here because its lease is held elsewhere; it is asked for again on the next interval.")]
    private static partial void LogMoveHeldElsewhere(ILogger logger);
}

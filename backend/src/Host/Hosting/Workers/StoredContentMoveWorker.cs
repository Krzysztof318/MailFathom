// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.Persistence;
using MailFathom.Host.Configuration.Persistence;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Carries the move of stored content one bounded pass per interval, for as long as the host is up.</summary>
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
/// Registered only where the deployment selected the object backend, because there is nowhere else to carry content to.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class StoredContentMoveWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ContentMoveOptions settings;
    private readonly ILogger<StoredContentMoveWorker> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the worker.</summary>
    /// <param name="scopeFactory">Opens the scope one pass runs inside.</param>
    /// <param name="settings">The interval between passes, and what one pass may carry.</param>
    /// <param name="logger">Records what a pass carried, in counts alone.</param>
    /// <param name="timeProvider">Drives the interval.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public StoredContentMoveWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<ContentMoveOptions> settings,
        ILogger<StoredContentMoveWorker> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.scopeFactory = scopeFactory;
        this.settings = settings.Value;
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(this.settings.Interval, this.timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await this.RunOnceAsync(stoppingToken);
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
            using var scope = this.scopeFactory.CreateScope();

            var move = scope.ServiceProvider.GetRequiredService<StoredContentMove>();
            var pass = await move.RunAsync(cancellationToken);

            if (pass.CopiedPayloadCount > 0 || pass.FailedPayloadCount > 0)
            {
                this.LogPassCarried(pass.CopiedPayloadCount, pass.MovedByteCount, pass.FailedPayloadCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown rather than a failure, exactly as an interrupted synchronization cycle is, so a rolling restart
            // does not read as a move that broke.
        }
        catch (PersistenceConcurrencyConflictException exception)
        {
            this.LogPassDeferredAfterConcurrencyConflict(exception);
        }
        catch (Exception exception)
        {
            this.LogPassFailed(exception);
        }
    }

    /// <summary>Reports one pass in counts only; no key, identity, or fragment of a message may reach a log.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Moved {CopiedPayloadCount} stored payloads carrying {MovedByteCount} bytes into the object backend, and left {FailedPayloadCount} of them in the database.")]
    private partial void LogPassCarried(long copiedPayloadCount, long movedByteCount, long failedPayloadCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Deferred a pass of the stored-content move after an unresolved optimistic concurrency conflict; the next interval will resume from the committed position.")]
    private partial void LogPassDeferredAfterConcurrencyConflict(Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A pass of the stored-content move failed; the next interval will resume from the committed position.")]
    private partial void LogPassFailed(Exception exception);
}

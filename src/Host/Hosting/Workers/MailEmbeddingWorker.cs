// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Observability;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Embeds each message synchronization has committed, one scoped work unit at a time.</summary>
/// <remarks>
/// <para>
/// The worker exists so that generating a vector is neither part of a mailbox fetch nor part of an MCP request. It waits
/// on the backlog rather than polling a table, takes one message at a time, and does its work in a scope of its own, so a
/// slow provider makes the backlog deeper instead of making anything else slower.
/// </para>
/// <para>
/// One message at a time is a decision rather than a placeholder. Concurrency here would multiply against the provider's
/// own rate limit and against the resilience pipeline's concurrency budget, and the bound that matters for keeping up
/// with mail is how many passages one request carries, which the generator already applies.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class MailEmbeddingWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IEmailEmbeddingBacklog backlog;
    private readonly EmailEmbeddingTelemetry telemetry;
    private readonly ILogger<MailEmbeddingWorker> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new embedding worker.</summary>
    public MailEmbeddingWorker(
        IServiceScopeFactory scopeFactory,
        IEmailEmbeddingBacklog backlog,
        EmailEmbeddingTelemetry telemetry,
        ILogger<MailEmbeddingWorker> logger,
        TimeProvider timeProvider)
    {
        this.scopeFactory = scopeFactory;
        this.backlog = backlog;
        this.telemetry = telemetry;
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.LogWorkerStarted();

        await foreach (var storedEmailId in this.backlog.ReadAllAsync(stoppingToken))
        {
            await this.EmbedOneAsync(storedEmailId, stoppingToken);
        }
    }

    /// <summary>Embeds one message and reports it, isolating whatever goes wrong from the messages behind it.</summary>
    /// <remarks>
    /// A failure ends this message's turn and nothing else. The passages it did not reach keep having no vector, which
    /// is exactly the condition the backfill selects on, so nothing is lost by declining to try again here — and trying
    /// again here would put a second retry layer around a call that already runs under one.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The hosted worker isolates one message's failure so the messages waiting behind it are still embedded.")]
    private async Task EmbedOneAsync(StoredEmailId storedEmailId, CancellationToken cancellationToken)
    {
        var startingTimestamp = this.timeProvider.GetTimestamp();

        try
        {
            using var scope = this.scopeFactory.CreateScope();

            var generator = scope.ServiceProvider.GetRequiredService<StoredEmailEmbeddingGenerator>();
            var run = await generator.EmbedAsync(storedEmailId, cancellationToken);

            this.telemetry.RecordEmbeddedMessage(run, this.timeProvider.GetElapsedTime(startingTimestamp));
            this.Report(run);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PersistenceConcurrencyConflictException exception)
        {
            this.LogMessageDeferredAfterConcurrencyConflict(exception);
        }
        catch (Exception exception)
        {
            this.LogMessageFailed(exception);
        }
    }

    /// <summary>Says what the outcome means for an operator, at the level that outcome deserves.</summary>
    /// <remarks>
    /// A message embedded and a message an instance embeds nothing at all for are both ordinary, so neither is a
    /// warning. The two conditions that need an operator are: a declaration that disagrees with what was activated, and
    /// a provider that refused.
    /// </remarks>
    private void Report(StoredEmailEmbeddingRun run)
    {
        switch (run.Outcome)
        {
            case StoredEmailEmbeddingOutcome.Embedded when run.EmbeddedChunkCount > 0:
                this.LogMessageEmbedded(run.EmbeddedChunkCount, this.backlog.Depth);

                break;

            case StoredEmailEmbeddingOutcome.NoActiveProfile:
                this.LogNoActiveProfile();

                break;

            case StoredEmailEmbeddingOutcome.GeneratorDisagreesWithProfile:
                this.LogGeneratorDisagreesWithProfile();

                break;

            // The classification is matched rather than defaulted, because inventing one would report a failure the
            // provider never gave. A ProviderFailed result without it is unconstructible, so the case simply does not
            // arise.
            case StoredEmailEmbeddingOutcome.ProviderFailed when run.Failure is { } failure:
                this.LogProviderFailed(failure, run.EmbeddedChunkCount);

                break;

            default:
                break;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The embedding worker is waiting for newly synchronized mail.")]
    private partial void LogWorkerStarted();

    /// <summary>Reports one message in counts only; no subject, address, passage, or vector may reach a log.</summary>
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Embedded {EmbeddedChunkCount} passages of one message; {BacklogDepth} messages are waiting.")]
    private partial void LogMessageEmbedded(int embeddedChunkCount, int backlogDepth);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "No embedding profile is active, so the message was not embedded. Activating a declared profile is what starts semantic search.")]
    private partial void LogNoActiveProfile();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The configured embedding model is not the one the active profile records, so nothing was embedded. Activate the current declaration, or restore the one the stored vectors belong to.")]
    private partial void LogGeneratorDisagreesWithProfile();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "An embedding provider call failed with {Failure} after {EmbeddedChunkCount} passages of this message were committed; the rest stay outstanding for the backfill.")]
    private partial void LogProviderFailed(EmbeddingGenerationFailure failure, int embeddedChunkCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Embedding one message was abandoned after an unresolved optimistic concurrency conflict; its remaining passages stay outstanding.")]
    private partial void LogMessageDeferredAfterConcurrencyConflict(Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Embedding one message failed; its remaining passages stay outstanding.")]
    private partial void LogMessageFailed(Exception exception);
}

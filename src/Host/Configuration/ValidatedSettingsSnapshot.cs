// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Configuration;

/// <summary>Publishes the settings snapshot new operations run with, and adopts a reloaded one once it is proven usable.</summary>
/// <typeparam name="TSettings">The bound settings group this publishes.</typeparam>
/// <remarks>
/// <para>
/// A configuration reload can rewrite a secret reference, so a snapshot is a candidate until every reference in it has
/// resolved. A candidate that fails validation is discarded and the last known good snapshot stays active, which is
/// what <see href="../../../docs/decisions/0002-configuration-reading-mapping-and-reload-boundary.md">ADR 0002</see>
/// requires of a reloadable setting group and what keeps a mistyped credential name from taking a running deployment
/// offline.
/// </para>
/// <para>
/// Validation never runs on the thread that reported the change. Configuration providers raise reloads from a file
/// watcher or a provider callback, and resolving a reference can reach a file, an environment block, or one day a
/// network-backed store; doing that inline would stall the provider and, on a failure, surface an exception on a
/// thread with nowhere to report it. Candidates are handed to a single background reader through a channel that keeps
/// only the newest one, so a burst of reloads costs one validation rather than a queue of stale ones.
/// </para>
/// <para>
/// The initial snapshot is published without being validated here, because the startup validator has already proven
/// the whole deployment's secret configuration and failed the host if it could not. Validating it again would double
/// every startup retrieval for no additional guarantee.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class ValidatedSettingsSnapshot<TSettings> :
    ISettingsSnapshot<TSettings>,
    IHostedLifecycleService,
    IAsyncDisposable
    where TSettings : class
{
    private readonly IOptionsMonitor<TSettings> optionsMonitor;
    private readonly Func<TSettings, CancellationToken, Task<IReadOnlyList<string>>> findConfigurationErrorsAsync;
    private readonly string settingsName;
    private readonly ILogger<ValidatedSettingsSnapshot<TSettings>> logger;
    private readonly CancellationTokenSource validationCancellation = new();

    private readonly Channel<ReloadCandidate> candidates = Channel.CreateBounded<ReloadCandidate>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    private TSettings publishedSettings;
    private long publishedSequence;
    private long observedChangeCount;
    private IDisposable? changeSubscription;
    private Task? validationLoop;
    private bool disposed;

    /// <summary>Initializes a new validated snapshot from the settings bound at startup.</summary>
    /// <param name="optionsMonitor">The bound settings and the reloads reported for them.</param>
    /// <param name="findConfigurationErrorsAsync">Proves a candidate usable, returning one message per setting an operator must fix.</param>
    /// <param name="settingsName">The configuration section name, which every log line about this group carries.</param>
    /// <param name="logger">The reload logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public ValidatedSettingsSnapshot(
        IOptionsMonitor<TSettings> optionsMonitor,
        Func<TSettings, CancellationToken, Task<IReadOnlyList<string>>> findConfigurationErrorsAsync,
        string settingsName,
        ILogger<ValidatedSettingsSnapshot<TSettings>> logger)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(findConfigurationErrorsAsync);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsName);
        ArgumentNullException.ThrowIfNull(logger);

        this.optionsMonitor = optionsMonitor;
        this.findConfigurationErrorsAsync = findConfigurationErrorsAsync;
        this.settingsName = settingsName;
        this.logger = logger;
        this.publishedSettings = optionsMonitor.CurrentValue;
    }

    /// <inheritdoc />
    public TSettings Current => Volatile.Read(ref this.publishedSettings);

    /// <inheritdoc />
    /// <inheritdoc />
    /// <remarks>
    /// The current value is re-read after subscribing, because hosted services are constructed before any lifecycle
    /// callback runs and the secret startup gate performs asynchronous checks in between. A reload landing in that
    /// window moves the monitor's value while no listener exists yet, and without this the publisher would hold the
    /// captured snapshot for the process lifetime, neither adopting nor rejecting the reload.
    /// </remarks>
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        this.changeSubscription = this.optionsMonitor.OnChange(this.AcceptCandidate);
        this.validationLoop = Task.Run(
            () => this.ValidateCandidatesAsync(this.validationCancellation.Token),
            CancellationToken.None);

        this.AcceptReloadMissedBeforeSubscribing();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => this.StopAcceptingCandidatesAsync(cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Idempotent, because the host stops the loop through the lifecycle and the container disposes the instance
    /// afterwards. Unlike the lifecycle stop, this cancels first rather than draining: by the time the container
    /// disposes, a retrieval still running is one that never returned, and waiting on it would hold up the shutdown it
    /// is already delaying.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        await this.validationCancellation.CancelAsync();
        await this.StopAcceptingCandidatesAsync(CancellationToken.None);

        this.validationCancellation.Dispose();
    }

    /// <summary>Takes a reloaded snapshot without validating it, so the reporting thread is never held up.</summary>
    /// <remarks>
    /// The channel drops an older waiting candidate rather than blocking, because only the newest snapshot is worth
    /// validating: an intermediate one is already superseded by the time the reader would reach it. Named options are
    /// ignored because this settings group has no named variants; adopting one would publish a snapshot bound from a
    /// section nothing else reads.
    /// </remarks>
    private void AcceptCandidate(TSettings candidate, string? name)
    {
        if (!string.IsNullOrEmpty(name))
        {
            return;
        }

        var sequence = Interlocked.Increment(ref this.observedChangeCount);

        this.candidates.Writer.TryWrite(new ReloadCandidate(sequence, candidate));
    }

    private void AcceptReloadMissedBeforeSubscribing()
    {
        var currentlyBound = this.optionsMonitor.CurrentValue;

        if (!ReferenceEquals(currentlyBound, this.Current))
        {
            this.AcceptCandidate(currentlyBound, name: null);
        }
    }

    private async Task ValidateCandidatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var candidate in this.candidates.Reader.ReadAllAsync(cancellationToken))
            {
                await this.PublishWhenUsableAsync(candidate, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown, not a configuration problem.
        }
    }

    /// <summary>Validates one candidate and publishes it only when it is both usable and newer than what is active.</summary>
    /// <remarks>
    /// A rejected candidate must not stop the loop, because the next reload is the operator's correction and a process
    /// that terminated on the mistake would never see it. The sequence guard is what keeps a slow validation of an
    /// older snapshot from overwriting a newer one that already published.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A candidate that fails for any reason must leave the previous snapshot active rather than end the reload loop or the process.")]
    internal async Task PublishWhenUsableAsync(ReloadCandidate candidate, CancellationToken cancellationToken)
    {
        try
        {
            var errors = await this.findConfigurationErrorsAsync(candidate.Settings, cancellationToken);
            if (errors.Count > 0)
            {
                this.LogReloadRejected(this.settingsName, string.Join(" ", errors));

                return;
            }

            // Two guards, because a candidate can be stale in two directions: one already published is behind, and one
            // superseded while it was being validated is about to be. Publishing the latter would put a credential the
            // operator has already replaced in front of every connection started before the newer candidate lands.
            if (candidate.Sequence <= Volatile.Read(ref this.publishedSequence)
                || candidate.Sequence < Volatile.Read(ref this.observedChangeCount))
            {
                return;
            }

            Volatile.Write(ref this.publishedSequence, candidate.Sequence);
            Volatile.Write(ref this.publishedSettings, candidate.Settings);

            this.LogReloadPublished(this.settingsName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The exception itself is deliberately not logged. A file, vault, or future provider failure carries the
            // target path, request URI, or credential identifier in its message and stack trace, and every ordinary
            // logging provider renders both. Only the failure's type crosses into the log.
            this.LogReloadRejectedUnexpectedly(this.settingsName, exception.GetType().Name);
        }
    }

    /// <summary>Stops taking reloads and lets the reader finish the one candidate that may still be waiting.</summary>
    /// <remarks>
    /// The writer is completed rather than the reader cancelled, so a candidate that arrived just before shutdown is
    /// still decided instead of being abandoned half-validated. At most one can be waiting, because the channel keeps
    /// only the newest, and the cancellation token remains the escape hatch for a retrieval that never returns.
    /// </remarks>
    private async Task StopAcceptingCandidatesAsync(CancellationToken shutdownToken)
    {
        this.changeSubscription?.Dispose();
        this.changeSubscription = null;

        if (this.validationLoop is not { } loop)
        {
            return;
        }

        this.validationLoop = null;
        this.candidates.Writer.TryComplete();

        // The host's own token is what stops a credential source that never returns from holding shutdown open. It is
        // registered rather than awaited alongside, so cancelling actually reaches the retrieval instead of merely
        // abandoning the wait on a loop that keeps running.
        await using var shutdownAbandonsValidation = shutdownToken.Register(
            static state => ((CancellationTokenSource)state!).Cancel(),
            this.validationCancellation);

        await loop;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Adopted a reloaded {SettingsName} configuration; new operations use its secret references.")]
    private partial void LogReloadPublished(string settingsName);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Rejected a reloaded {SettingsName} configuration and kept the previous one active. {ConfigurationErrors}")]
    private partial void LogReloadRejected(string settingsName, string configurationErrors);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Rejected a reloaded {SettingsName} configuration after an unexpected {FailureType} and kept the previous one active. The failure detail is withheld because a credential provider's exception can carry the reference target.")]
    private partial void LogReloadRejectedUnexpectedly(string settingsName, string failureType);

    /// <summary>A reloaded snapshot waiting to be validated, tagged with the order in which it arrived.</summary>
    /// <param name="Sequence">The position in the order the reloads were reported in.</param>
    /// <param name="Settings">The bound snapshot, not yet proven usable.</param>
    internal sealed record ReloadCandidate(long Sequence, TSettings Settings);
}

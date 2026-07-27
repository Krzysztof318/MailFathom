// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using MailMcp.Application.Mail;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Transport;
using Microsoft.Extensions.Options;

namespace MailMcp.Host.Configuration;

/// <summary>Publishes the mail synchronization snapshot new operations run with, and adopts a reloaded one once it is proven usable.</summary>
/// <remarks>
/// <para>
/// A configuration reload can rewrite a secret reference, so a snapshot is a candidate until every reference in it has
/// resolved. A candidate that fails validation is discarded and the last known good snapshot stays active, which is
/// what ADR 0002 requires of a reloadable setting group and what keeps a mistyped credential name from taking a
/// running deployment offline.
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
internal sealed partial class ValidatedMailSynchronizationSettings :
    IMailSynchronizationSettingsReader,
    IMailTransportSecurityPolicyReader,
    IHostedLifecycleService,
    IAsyncDisposable
{
    private readonly IOptionsMonitor<MailSynchronizationOptions> optionsMonitor;
    private readonly SecretConfigurationValidator validator;
    private readonly ILogger<ValidatedMailSynchronizationSettings> logger;
    private readonly CancellationTokenSource validationCancellation = new();

    private readonly Channel<ReloadCandidate> candidates = Channel.CreateBounded<ReloadCandidate>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    private MailSynchronizationOptions publishedSettings;
    private long publishedSequence;
    private long observedChangeCount;
    private IDisposable? changeSubscription;
    private Task? validationLoop;
    private bool disposed;

    /// <summary>Initializes new validated mail synchronization settings from the snapshot bound at startup.</summary>
    public ValidatedMailSynchronizationSettings(
        IOptionsMonitor<MailSynchronizationOptions> optionsMonitor,
        SecretConfigurationValidator validator,
        ILogger<ValidatedMailSynchronizationSettings> logger)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);

        this.optionsMonitor = optionsMonitor;
        this.validator = validator;
        this.logger = logger;
        this.publishedSettings = optionsMonitor.CurrentValue;
    }

    /// <inheritdoc />
    public MailSynchronizationOptions Current => Volatile.Read(ref this.publishedSettings);

    /// <inheritdoc />
    public MailTransportSecurityPolicy GetPolicy(MailAccountId accountId) => this.Current.GetPolicy(accountId);

    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        this.changeSubscription = this.optionsMonitor.OnChange(this.AcceptCandidate);
        this.validationLoop = Task.Run(
            () => this.ValidateCandidatesAsync(this.validationCancellation.Token),
            CancellationToken.None);

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
    public Task StoppedAsync(CancellationToken cancellationToken) => this.StopAcceptingCandidatesAsync();

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
        await this.StopAcceptingCandidatesAsync();

        this.validationCancellation.Dispose();
    }

    /// <summary>Takes a reloaded snapshot without validating it, so the reporting thread is never held up.</summary>
    /// <remarks>
    /// The channel drops an older waiting candidate rather than blocking, because only the newest snapshot is worth
    /// validating: an intermediate one is already superseded by the time the reader would reach it. Named options are
    /// ignored because this settings group has no named variants; adopting one would publish a snapshot bound from a
    /// section nothing else reads.
    /// </remarks>
    private void AcceptCandidate(MailSynchronizationOptions candidate, string? name)
    {
        if (!string.IsNullOrEmpty(name))
        {
            return;
        }

        var sequence = Interlocked.Increment(ref this.observedChangeCount);

        this.candidates.Writer.TryWrite(new ReloadCandidate(sequence, candidate));
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
            var errors = await this.validator.FindMailConfigurationErrorsAsync(candidate.Settings, cancellationToken);
            if (errors.Count > 0)
            {
                this.LogReloadRejected(string.Join(" ", errors));

                return;
            }

            if (candidate.Sequence <= Volatile.Read(ref this.publishedSequence))
            {
                return;
            }

            Volatile.Write(ref this.publishedSequence, candidate.Sequence);
            Volatile.Write(ref this.publishedSettings, candidate.Settings);

            this.LogReloadPublished();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.LogReloadRejectedUnexpectedly(exception);
        }
    }

    /// <summary>Stops taking reloads and lets the reader finish the one candidate that may still be waiting.</summary>
    /// <remarks>
    /// The writer is completed rather than the reader cancelled, so a candidate that arrived just before shutdown is
    /// still decided instead of being abandoned half-validated. At most one can be waiting, because the channel keeps
    /// only the newest, and the cancellation token remains the escape hatch for a retrieval that never returns.
    /// </remarks>
    private async Task StopAcceptingCandidatesAsync()
    {
        this.changeSubscription?.Dispose();
        this.changeSubscription = null;

        if (this.validationLoop is not { } loop)
        {
            return;
        }

        this.validationLoop = null;
        this.candidates.Writer.TryComplete();
        await loop;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Adopted a reloaded mail synchronization configuration; new operations use its secret references.")]
    private partial void LogReloadPublished();

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Rejected a reloaded mail synchronization configuration and kept the previous one active. {ConfigurationErrors}")]
    private partial void LogReloadRejected(string configurationErrors);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Rejected a reloaded mail synchronization configuration after an unexpected failure and kept the previous one active.")]
    private partial void LogReloadRejectedUnexpectedly(Exception exception);

    /// <summary>A reloaded snapshot waiting to be validated, tagged with the order in which it arrived.</summary>
    /// <param name="Sequence">The position in the order the reloads were reported in.</param>
    /// <param name="Settings">The bound snapshot, not yet proven usable.</param>
    internal sealed record ReloadCandidate(long Sequence, MailSynchronizationOptions Settings);
}

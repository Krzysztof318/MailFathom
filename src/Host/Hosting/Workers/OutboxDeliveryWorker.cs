// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Observability;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Delivers what an account has just been asked to send, as soon as it has been asked.</summary>
/// <remarks>
/// <para>
/// The loop owns promptness and nothing else. Everything outstanding is drained by the account's own synchronization
/// run, which is what makes the outbox correct without this worker at all; what a run cannot do is leave in seconds,
/// and a message somebody authored — or a tool call that answered with a queued identifier — must not wait behind a
/// mailbox scan.
/// </para>
/// <para>
/// It waits on the signal rather than polling, so an instance with nothing to send costs nothing at all, and a pass
/// that filled its batch signals the account again so a backlog is drained rather than trickled one signal at a time.
/// </para>
/// <para>
/// One account at a time. A pass already attempts its sends one after another because they share a submission server,
/// and a second loop beside this one would be a second unstated bound on how many connections this deployment opens to
/// the providers it sends through.
/// </para>
/// <para>
/// What each pass did is reported to the log and to the delivery instruments, because the two are read at different
/// distances: a line names one send and an instrument names a rate. Neither carries an address, a subject, or anything
/// a submission server wrote.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class OutboxDeliveryWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly MailOutboxSignal signal;
    private readonly MailDeliveryTelemetry telemetry;
    private readonly ILogger<OutboxDeliveryWorker> logger;

    /// <summary>Initializes the worker that answers the outbox signal.</summary>
    /// <param name="scopeFactory">Creates the scope each account's pass runs in.</param>
    /// <param name="signal">Says which account has something to send.</param>
    /// <param name="telemetry">Publishes what each pass did as the counts an operator reads without opening a log.</param>
    /// <param name="logger">Records pass outcomes, which carry account aliases and no message-level data.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public OutboxDeliveryWorker(
        IServiceScopeFactory scopeFactory,
        MailOutboxSignal signal,
        MailDeliveryTelemetry telemetry,
        ILogger<OutboxDeliveryWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(logger);

        this.scopeFactory = scopeFactory;
        this.signal = signal;
        this.telemetry = telemetry;
        this.logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.LogWorkerStarted();

        try
        {
            await foreach (var accountId in this.signal.ReadAllAsync(stoppingToken))
            {
                var report = await this.RunPassAsync(accountId, stoppingToken);

                // A pass that took everything it was allowed left more behind it, so the account is signalled again
                // rather than waiting for its synchronization run to notice. A refused signal is harmless here for the
                // same reason it is everywhere else: that run is what picks the rest up.
                if (report.BatchFilled)
                {
                    this.signal.Signal(accountId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping; a send already claimed is given back by the pass that holds it.
        }
    }

    /// <summary>Runs one scoped pass over one account's outbox, isolating whatever goes wrong from the accounts beside it.</summary>
    /// <remarks>
    /// A failed pass keeps the loop alive on purpose. What can fail here is the claim itself — a send's own failure is
    /// already recorded against its record by the attempt — and a database that is briefly unavailable says nothing
    /// about whether there is mail to send. Anything the pass had claimed keeps its lease and is claimable again when
    /// that lease expires, so nothing is lost by leaving it to the account's next run.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "One account's pass must not stop the loop that serves every other account; each send's own record already carries how far it got, and the account's synchronization run drains what this pass did not.")]
    private async Task<MailOutboxPassReport> RunPassAsync(MailAccountId accountId, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = this.scopeFactory.CreateScope();

            var pass = scope.ServiceProvider.GetRequiredService<MailOutboxPass>();
            var report = await pass.RunAsync(accountId, stoppingToken);

            this.telemetry.Report(accountId, report);
            this.Report(accountId, report);

            return report;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return MailOutboxPassReport.Empty;
        }
        catch (PersistenceConcurrencyConflictException exception)
        {
            this.LogPassDeferredAfterConcurrencyConflict(exception, accountId.Value);

            return MailOutboxPassReport.Empty;
        }
        catch (Exception exception)
        {
            this.LogPassFailed(exception, accountId.Value);

            return MailOutboxPassReport.Empty;
        }
    }

    /// <summary>Says what a pass did, at the level each outcome deserves.</summary>
    /// <remarks>
    /// A pass that delivered nothing says nothing at all, because an empty outbox is the ordinary state of an account
    /// and a line per signal would be the whole log. What is reported is each send that ended, and the one ending that
    /// waits for a person is reported at the level that reaches an operator.
    /// </remarks>
    private void Report(MailAccountId accountId, MailOutboxPassReport report)
    {
        if (report.MarkedUnknownCount > 0)
        {
            this.LogUnknownOutcomesMarked(accountId.Value, report.MarkedUnknownCount);
        }

        foreach (var result in report.Results)
        {
            switch (result.Outcome)
            {
                case MailOutboxDeliveryOutcome.Sent:
                    this.LogSendDelivered(accountId.Value, result.AttemptCount);

                    break;

                case MailOutboxDeliveryOutcome.Deferred:
                    this.LogSendDeferred(accountId.Value, result.AttemptCount, FailureCodeOf(result));

                    break;

                case MailOutboxDeliveryOutcome.Refused:
                    this.LogSendRefused(
                        accountId.Value,
                        result.AttemptCount,
                        FailureCodeOf(result),
                        result.ReplyCode);

                    break;

                case MailOutboxDeliveryOutcome.OutcomeUnknown:
                    this.LogSendOutcomeUnknown(accountId.Value, result.AttemptCount);

                    break;

                case MailOutboxDeliveryOutcome.LeaseLost:
                    this.LogSendLeaseLost(accountId.Value, result.AttemptCount);

                    break;

                case MailOutboxDeliveryOutcome.NotRecorded:
                    this.LogSendNotRecorded(accountId.Value, result.AttemptCount, FailureCodeOf(result));

                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>Names the failure a send ended on, as the code an operator looks up rather than as a message.</summary>
    private static int? FailureCodeOf(MailOutboxDeliveryResult result) => result.Failure?.Value;

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The outbox delivery worker is running; a message written down is delivered as soon as its account is signalled, and the account's own run drains whatever a signal missed.")]
    private partial void LogWorkerStarted();

    /// <summary>Reports one delivered send by its account and attempt; a recipient names a person and never reaches a log.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "A message queued for account {AccountId} was accepted by its submission server on attempt {AttemptCount}.")]
    private partial void LogSendDelivered(string accountId, int attemptCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A message queued for account {AccountId} was not transmitted on attempt {AttemptCount} [failure {FailureCode}] and is claimable again once its backoff has passed.")]
    private partial void LogSendDeferred(string accountId, int attemptCount, int? failureCode);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "A message queued for account {AccountId} will not be offered again after attempt {AttemptCount} [failure {FailureCode}, reply {ReplyCode}]. What each recipient was told is on the send's own record.")]
    private partial void LogSendRefused(string accountId, int attemptCount, int? failureCode, int? replyCode);

    /// <summary>Reports the one ending that waits for a person rather than for another attempt.</summary>
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "A message queued for account {AccountId} went out on attempt {AttemptCount} and its submission server never answered, so whether the recipients received it is unknown. It is not transmitted again, and it stays visible in the outbox until somebody decides what to do with it.")]
    private partial void LogSendOutcomeUnknown(string accountId, int attemptCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A message queued for account {AccountId} had moved to another attempt by the time attempt {AttemptCount} finished, so nothing was recorded for it. The attempt that holds it now is the one whose outcome counts.")]
    private partial void LogSendLeaseLost(string accountId, int attemptCount);

    /// <summary>Reports a send whose attempt ended with the store unable to take the answer.</summary>
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The outcome of attempt {AttemptCount} for a message queued for account {AccountId} could not be written down [failure {FailureCode}], so its record stands where the failed write left it and its lease is what frees it for another attempt.")]
    private partial void LogSendNotRecorded(string accountId, int attemptCount, int? failureCode);

    /// <summary>Reports the sweep's discovery at the level the account's own run reports it at, for the same reason.</summary>
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "{MarkedCount} message(s) queued for account {AccountId} were found mid-transmission with no attempt left holding them, and are recorded as having an unknown outcome. Each one may or may not have been delivered; none is transmitted again.")]
    private partial void LogUnknownOutcomesMarked(string accountId, int markedCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The outbox pass for account {AccountId} was deferred after an unresolved optimistic concurrency conflict; the next signal or synchronization run claims again.")]
    private partial void LogPassDeferredAfterConcurrencyConflict(Exception exception, string accountId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The outbox pass for account {AccountId} failed; anything it was holding stays leased until the lease expires and the next pass claims again.")]
    private partial void LogPassFailed(Exception exception, string accountId);
}

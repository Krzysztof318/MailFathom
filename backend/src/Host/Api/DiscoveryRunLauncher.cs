// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Application.Discovery.Streaming;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Transport;

namespace MailFathom.Host.Api;

/// <summary>Runs one Discover run past the request that asked for it.</summary>
/// <remarks>
/// <para>
/// A run is reached over one request that asks the question and however many read the answer back, so the work belongs
/// to none of them. This puts it on a scope of its own that lives as long as the run, and it is in the composition root
/// because that is where a scope is made and where a caller is stated onto one.
/// </para>
/// <para>
/// <strong>The caller travels with the run.</strong> The request's own scope is gone by the time the run executes, so
/// the principal the transport admitted is stated onto the run's scope rather than inherited: the use case reads the
/// grant and the user from it exactly as it would inside a request, and a run therefore refuses in the background
/// whatever it would have refused in the foreground.
/// </para>
/// <para>
/// What this bounds is the process stopping, because a run holds a database connection and there is nobody left to
/// answer. The longest a run may take is the run's own and is applied inside it, so the two endings stay distinguishable
/// to the client. Neither is the client's connection: a person who closed the page does not stop a run, because they may
/// reattach to it.
/// </para>
/// <para>
/// <strong>It is also where a run's unnamed failure is recorded.</strong> The use case publishes every ending it can
/// name and lets the rest propagate, because <c>Application</c> holds no logger and a failure nothing writes down is one
/// <see cref="DiscoveryRunFailure.Failed" /> promises an operator can read. Here there is a logger, so the fault is
/// logged and the run is ended on it.
/// </para>
/// </remarks>
internal sealed partial class DiscoveryRunLauncher
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IDiscoveryRunStore runs;
    private readonly ClientSignals signals;
    private readonly ExecutingDiscoveryRuns executing;
    private readonly IHostApplicationLifetime lifetime;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<DiscoveryRunLauncher> logger;

    /// <summary>Initializes the launcher.</summary>
    /// <param name="scopeFactory">Makes the scope one run executes in.</param>
    /// <param name="runs">Where the run writes what it composes, which every replica reads it back from.</param>
    /// <param name="signals">Says over the hub how far the run has got, carrying no part of what it composed.</param>
    /// <param name="executing">The runs this replica is executing, which a stop that lands here reaches the work through.</param>
    /// <param name="lifetime">Reports that the process is stopping, which ends every run it is executing.</param>
    /// <param name="timeProvider">Stamps each write, which the run's retention window is measured from.</param>
    /// <param name="logger">Reports the failures a run cannot name to its client, which nothing else would.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public DiscoveryRunLauncher(
        IServiceScopeFactory scopeFactory,
        IDiscoveryRunStore runs,
        ClientSignals signals,
        ExecutingDiscoveryRuns executing,
        IHostApplicationLifetime lifetime,
        TimeProvider timeProvider,
        ILogger<DiscoveryRunLauncher> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(executing);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.scopeFactory = scopeFactory;
        this.runs = runs;
        this.signals = signals;
        this.executing = executing;
        this.lifetime = lifetime;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <summary>Starts a run and returns without waiting for it.</summary>
    /// <param name="question">The question and the resolved scope bounding what may be read to answer it.</param>
    /// <param name="id">The run the caller has already opened, which is what the client was handed.</param>
    /// <param name="user">Whose question it is, which is who may read the run and whose screens its advances reach.</param>
    /// <param name="caller">The principal the transport admitted, which the run executes under.</param>
    /// <returns>The task this process finishes with the run on, which the request that asked the question ignores.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The route awaits nothing and observes nothing, because the run reports itself: every ending it can reach is
    /// written into its own journal, so a task result would carry what the client can already read. What the task does
    /// say is when this process has finished with the run — the ending is written a moment before the journal is
    /// released — which is why it is handed back rather than discarded here.
    /// </remarks>
    internal Task Start(MailQuestion question, DiscoveryRunId id, UserId user, AuthorizedPrincipal caller)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(caller);

        return Task.Run(() => this.ExecuteAsync(question, id, user, caller));
    }

    /// <summary>Executes one run on a scope of its own and leaves it ended however it went.</summary>
    /// <remarks>
    /// <para>
    /// The handler here is for the two endings the run cannot state to its client: a scope this process could not
    /// compose the use case out of, and a fault the use case has no name for. Both are the deployment's rather than the
    /// question's, both are what <see cref="DiscoveryRunFailure.Failed" /> stands for, and nothing else would report
    /// either — nothing awaits this task, so an exception escaping here would be observed by nobody and would leave a
    /// client reading a run that never ends.
    /// </para>
    /// <para>
    /// <strong>The ending in the cleanup is the one that is not about a failure at all.</strong> It writes nothing on
    /// every ordinary path, the run having already ended itself. What it is for is the execution stopping without
    /// either the run or the handler above reaching an ending — a cancellation observed outside the use case, a
    /// process shutting down between the two — and what it leaves instead of a run pending forever is
    /// <see cref="DiscoveryRunFailure.Stopped" />, which is the word for the deployment having gone away mid-run. A
    /// replica killed outright writes nothing at all, and the run's own ceiling is what closes that one.
    /// </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Nothing awaits this task, so an escaping exception would be observed by nobody and would leave the run unended; it is logged and the run is ended instead.")]
    private async Task ExecuteAsync(
        MailQuestion question,
        DiscoveryRunId id,
        UserId user,
        AuthorizedPrincipal caller)
    {
        using var journal = new DiscoveryRunJournal(id, user, this.runs, this.signals, this.timeProvider);

        // Held outside the try so the ending can still say what the run spent. A scope this process could not compose
        // has spent nothing and reports nothing; a run that faulted on its second provider call has spent what it spent,
        // and an ending claiming otherwise would be the one place a cost figure lies.
        MailAnsweringRunLedger? ledger = null;

        this.executing.Register(journal);

        try
        {
            await using var scope = this.scopeFactory.CreateAsyncScope();

            scope.ServiceProvider.GetRequiredService<TransportAuthorizedPrincipalSource>().Assume(caller);
            ledger = scope.ServiceProvider.GetRequiredService<MailAnsweringRunLedger>();

            await scope.ServiceProvider
                .GetRequiredService<WatchedDiscoveryRun>()
                .RunAsync(question, journal, this.lifetime.ApplicationStopping);
        }
        catch (Exception failure)
        {
            this.LogRunFailedWithoutANameForIt(failure);

            await journal.AppendAsync(
                new DiscoveryRunFailed(
                    DiscoveryRunFailure.Failed,
                    ledger?.Read() ?? MailAnsweringRunSpend.Nothing),
                CancellationToken.None);
        }
        finally
        {
            await this.EndIfStillRunningAsync(journal, ledger);

            this.executing.Release(journal.Id);
        }
    }

    /// <summary>Writes an ending for a run whose execution stopped without one, so nothing is left pending.</summary>
    /// <remarks>
    /// A no-op on every ordinary path, and conditional on nothing but this execution's own reading of whether it ended
    /// the run: the store refuses a second ending anyway, so writing one that was not needed costs a statement and
    /// writing none that was costs a client the run.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "This is the last thing that runs for a run; a failure here is logged rather than escaping into a task nobody awaits.")]
    private async Task EndIfStillRunningAsync(DiscoveryRunJournal journal, MailAnsweringRunLedger? ledger)
    {
        if (journal.HasEnded)
        {
            return;
        }

        try
        {
            await journal.AppendAsync(
                new DiscoveryRunFailed(
                    DiscoveryRunFailure.Stopped,
                    ledger?.Read() ?? MailAnsweringRunSpend.Nothing),
                CancellationToken.None);
        }
        catch (Exception failure)
        {
            this.LogRunCouldNotBeEnded(failure);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "A Discover run ended on a failure it had no name for, so its client was told only that it failed. "
            + "Neither the question nor the mail it read is in this record; what failed is this deployment's own "
            + "composition or a dependency it called.")]
    private partial void LogRunFailedWithoutANameForIt(Exception failure);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "A Discover run stopped executing and its ending could not be written, so a client reading it sees "
            + "a run still working until its own ceiling forgets it. Neither the question nor the mail it read is in "
            + "this record; what failed is the deployment's own store.")]
    private partial void LogRunCouldNotBeEnded(Exception failure);
}

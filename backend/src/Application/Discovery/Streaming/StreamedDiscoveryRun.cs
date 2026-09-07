// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;

namespace MailFathom.Application.Discovery.Streaming;

/// <summary>Runs one question and publishes what happens as it happens, including what it is spending while it spends it.</summary>
/// <remarks>
/// <para>
/// The use case a streamed Discover run is performed through. It decides nothing about the question — that is
/// <see cref="DiscoveryRun" />'s, unchanged — and owns one thing instead: that a person watching sees the run working,
/// reads a block before the rest arrives, keeps what arrived when the run ends badly, and can read what the run cost
/// while it is still running.
/// </para>
/// <para>
/// <strong>Every failure this type can name becomes the run's own ending</strong>, because by the time this executes the
/// request that asked the question has already been answered and there is nobody left to throw at. Which failure it was
/// is published as a closed value carrying nothing about the question or the mail. A failure it cannot name is left to
/// propagate deliberately: naming it would need a log to name it in, this layer has no logger by construction, and a
/// caller that records it is the only place <see cref="DiscoveryRunFailure.Failed" /> becomes readable to an operator.
/// </para>
/// <para>
/// A run publishes its sources before the blocks that name them, so a client can render a block the moment it arrives
/// rather than holding it until a plan closes. That ordering is this type's, and it is the reason the composition hands
/// citations and blocks over separately.
/// </para>
/// <para>
/// <strong>What it publishes about cost is the run's own consumption and never a price.</strong> The three counts and
/// the ceilings they are read against are what
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0022-what-an-ai-run-reports-about-cost-cancellation-and-the-model.md">ADR 0022</see>
/// settles on: the envelope goes out with the start, the counts accrue on the events the run already publishes, and the
/// final ones stay with the ending whichever ending it was. Every one of them is a number or a name an operator chose.
/// </para>
/// </remarks>
public sealed class StreamedDiscoveryRun
{
    private readonly DiscoveryRun run;
    private readonly MailAnsweringRunLedger ledger;
    private readonly MailAnsweringPeriodBounds periodBounds;
    private readonly TimeProvider timeProvider;
    private readonly AnsweringEndpointIdentity? endpoint;

    /// <summary>Initializes the use case one streamed run is performed through.</summary>
    /// <param name="run">The run itself, which decides what to retrieve and retrieves it.</param>
    /// <param name="ledger">Counts what this run spends, which is one instance per run because the scope is the run.</param>
    /// <param name="periodBounds">What the runs of one period may add up to, read for the instant a refused period turns over.</param>
    /// <param name="timeProvider">Measures the longest a run may take, and places a refusal in its period.</param>
    /// <param name="endpoint">How the answering endpoint is named to the person who asked, absent on a deployment that answers no questions.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument but <paramref name="endpoint" /> is <see langword="null" />.</exception>
    public StreamedDiscoveryRun(
        DiscoveryRun run,
        MailAnsweringRunLedger ledger,
        MailAnsweringPeriodBounds periodBounds,
        TimeProvider timeProvider,
        AnsweringEndpointIdentity? endpoint)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(periodBounds);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.run = run;
        this.ledger = ledger;
        this.periodBounds = periodBounds;
        this.timeProvider = timeProvider;
        this.endpoint = endpoint;
    }

    /// <summary>Runs the question and publishes the run to its stream, ending it however it ends.</summary>
    /// <param name="question">The question and the scope bounding what may be read to answer it.</param>
    /// <param name="journal">Where the run publishes, which is what a client reads, reattaches to, and stops it through.</param>
    /// <param name="cancellationToken">Stops the run where it stands, which is how the deployment shutting down reaches it.</param>
    /// <returns>A task that completes once the run has published its ending.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="question" /> or <paramref name="journal" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Cancellation ends the run as a stated failure rather than as a cancelled task, because the caller is a background
    /// worker with nowhere to report one and because a client watching a run that was stopped has to be told that rather
    /// than left on a connection that closes silently.
    /// </para>
    /// <para>
    /// <strong>Which cancellation it was is part of what the client is told</strong>, and there are three of them. The
    /// person stopped the run, which ends it as <see cref="DiscoveryRunFailure.Cancelled" />; the run spent the longest
    /// a run may take, which ends it as <see cref="DiscoveryRunFailure.TimedOut" />; or the deployment stopped mid-run,
    /// which ends it as <see cref="DiscoveryRunFailure.Stopped" />. Each is a different fact for the person reading it —
    /// I stopped this, this question was more than one run could answer, and nothing about the question at all — so each
    /// gets a source of its own rather than being collapsed into whichever the caller happened to name.
    /// </para>
    /// <para>
    /// A stop reaches the provider call and the retrieval because it cancels the token those run under, which is the
    /// difference between stopping the spending and stopping the watching. What had already been published stays, and
    /// what had already been spent stays spent.
    /// </para>
    /// </remarks>
    public async Task RunAsync(
        MailQuestion question,
        DiscoveryRunJournal journal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(journal);

        journal.Append(new DiscoveryRunStarted
        {
            Bounds = this.ledger.Bounds,
            EndpointAlias = this.endpoint?.Alias ?? string.Empty,
            PublishedModel = this.endpoint?.PublishedModel ?? string.Empty,
        });

        using var budget = new CancellationTokenSource(DiscoveryRunBounds.MaximumDuration, this.timeProvider);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            budget.Token,
            journal.Stopping);

        try
        {
            var result = await this.run.RunAsync(
                question,
                progress => journal.Append(new DiscoveryRetrievalProgressed(progress, this.ledger.Read())),
                bounded.Token);

            journal.Append(new DiscoveryRunCompleted(
                Present(result, journal),
                result.Presentation.Coverage,
                this.ledger.Read()));
        }
        catch (OperationCanceledException) when (journal.Stopping.IsCancellationRequested)
        {
            // Read before the deployment's own stopping token, because somebody stopping their run is a fact about
            // their question and a shutdown that arrives in the same moment says nothing about it.
            this.End(journal, DiscoveryRunFailure.Cancelled);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            this.End(journal, DiscoveryRunFailure.Stopped);
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested)
        {
            this.End(journal, DiscoveryRunFailure.TimedOut);
        }
        catch (MailAnsweringBudgetExhaustedException spent)
        {
            this.EndOnCeiling(journal, spent);
        }
        catch (MailAnsweringUnavailableException unavailable)
        {
            this.End(
                journal,
                unavailable.Availability is MailAnsweringAvailability.Inactive
                    ? DiscoveryRunFailure.Unavailable
                    : DiscoveryRunFailure.TemporarilyUnavailable);
        }
        catch (MailboxQueryFilterInvalidException)
        {
            this.End(journal, DiscoveryRunFailure.RetrievalRefused);
        }
    }

    /// <summary>Publishes the composed plan a part at a time, source by source and then block by block.</summary>
    /// <remarks>
    /// <para>
    /// The plan is the run's own, composed by <see cref="DiscoveryRun" /> out of what it retrieved. Nothing is decided
    /// here beyond the order it goes out in: what a client assembles is exactly the plan the run produced, and a client
    /// that read the whole stream holds every part of it.
    /// </para>
    /// <para>
    /// A source refused by the stream's own bound takes the blocks resting on it with it, because a block naming a
    /// citation nobody declared is the one way a citation contract fails quietly. So the run stops at the first refusal
    /// and states that it published less than it composed, rather than publishing a block whose sources went missing.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<PresentationLimitation> Present(
        DiscoveryRunResult result,
        DiscoveryRunJournal journal)
    {
        foreach (var citation in result.Presentation.Citations)
        {
            if (!journal.Append(new DiscoveryCitationDeclared(citation)))
            {
                return [PresentationLimitation.BlocksOmitted];
            }
        }

        foreach (var block in result.Presentation.Blocks)
        {
            if (!journal.Append(new DiscoveryBlockComposed(block)))
            {
                return [PresentationLimitation.BlocksOmitted];
            }
        }

        return result.Presentation.Limitations;
    }

    /// <summary>Ends the run on a stated failure, with what it had spent by the time it reached it.</summary>
    private void End(DiscoveryRunJournal journal, DiscoveryRunFailure failure) =>
        journal.Append(new DiscoveryRunFailed(failure, this.ledger.Read()));

    /// <summary>Ends a run one of the two spend ceilings refused, as the state that ceiling is.</summary>
    /// <remarks>
    /// The period refusal names the instant its window turns over and the run refusal names nothing, which is the whole
    /// difference between them: waiting answers one and never the other. The instant is computed from the clock and the
    /// configured period rather than from what has been spent, so publishing it discloses nothing about the deployment's
    /// activity — and neither state names a consumed amount, for the reason
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0022-what-an-ai-run-reports-about-cost-cancellation-and-the-model.md">ADR 0022</see>
    /// gives: on a deployment serving several users the remaining allowance is a report of what the others have been
    /// doing.
    /// </remarks>
    private void EndOnCeiling(DiscoveryRunJournal journal, MailAnsweringBudgetExhaustedException spent) =>
        journal.Append(spent.Scope is MailAnsweringBudgetScope.Period
            ? new DiscoveryRunFailed(DiscoveryRunFailure.PeriodSpent, this.ledger.Read())
            {
                RetryAt = this.periodBounds.PeriodEndAt(this.timeProvider.GetUtcNow()),
            }
            : new DiscoveryRunFailed(DiscoveryRunFailure.RunSpent, this.ledger.Read()));
}

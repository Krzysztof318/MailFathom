// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;

namespace MailFathom.Application.Discovery.Streaming;

/// <summary>Runs one question and publishes what happens as it happens.</summary>
/// <remarks>
/// <para>
/// The use case a streamed Discover run is performed through. It decides nothing about the question — that is
/// <see cref="DiscoveryRun" />'s, unchanged — and owns one thing instead: that a person watching sees the run working,
/// reads a block before the rest arrives, and keeps what arrived when the run ends badly.
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
/// </remarks>
public sealed class StreamedDiscoveryRun
{
    private readonly DiscoveryRun run;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the use case one streamed run is performed through.</summary>
    /// <param name="run">The run itself, which decides what to retrieve and retrieves it.</param>
    /// <param name="timeProvider">Measures the longest a run may take, which this type rather than its caller applies.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="run" /> or <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public StreamedDiscoveryRun(DiscoveryRun run, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.run = run;
        this.timeProvider = timeProvider;
    }

    /// <summary>Runs the question and publishes the run to its stream, ending it however it ends.</summary>
    /// <param name="question">The question and the scope bounding what may be read to answer it.</param>
    /// <param name="journal">Where the run publishes, which is what a client reads and reattaches to.</param>
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
    /// <strong>Which cancellation it was is part of what the client is told.</strong> The longest a run may take is
    /// applied here, over a source of this type's own, so a run that spent it ends as
    /// <see cref="DiscoveryRunFailure.TimedOut" /> while a deployment stopping mid-run ends as
    /// <see cref="DiscoveryRunFailure.Stopped" /> — one says the question was too much to answer and the other says
    /// nothing about the question at all. A caller applying the bound to the token it passes would collapse the two into
    /// whichever the caller happened to name.
    /// </para>
    /// </remarks>
    public async Task RunAsync(
        MailQuestion question,
        DiscoveryRunJournal journal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(journal);

        journal.Append(new DiscoveryRunStarted());

        using var budget = new CancellationTokenSource(DiscoveryRunBounds.MaximumDuration, this.timeProvider);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);

        try
        {
            var result = await this.run.RunAsync(
                question,
                progress => journal.Append(new DiscoveryRetrievalProgressed(progress)),
                bounded.Token);

            journal.Append(new DiscoveryRunCompleted(Present(result, journal), result.Presentation.Coverage));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            journal.Append(new DiscoveryRunFailed(DiscoveryRunFailure.Stopped));
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested)
        {
            journal.Append(new DiscoveryRunFailed(DiscoveryRunFailure.TimedOut));
        }
        catch (MailAnsweringUnavailableException unavailable)
        {
            journal.Append(new DiscoveryRunFailed(
                unavailable.Availability is MailAnsweringAvailability.Inactive
                    ? DiscoveryRunFailure.Unavailable
                    : DiscoveryRunFailure.TemporarilyUnavailable));
        }
        catch (MailboxQueryFilterInvalidException)
        {
            journal.Append(new DiscoveryRunFailed(DiscoveryRunFailure.RetrievalRefused));
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
}

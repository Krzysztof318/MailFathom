// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
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
/// <strong>Nothing that ends the run reaches the caller.</strong> Every failure becomes the run's own ending, because
/// by the time this executes the request that asked the question has already been answered and there is nobody left to
/// throw at. Which failure it was is published as a closed value carrying nothing about the question or the mail.
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

    /// <summary>Initializes the use case one streamed run is performed through.</summary>
    /// <param name="run">The run itself, which decides what to retrieve and retrieves it.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="run" /> is <see langword="null" />.</exception>
    public StreamedDiscoveryRun(DiscoveryRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        this.run = run;
    }

    /// <summary>Runs the question and publishes the run to its stream, ending it however it ends.</summary>
    /// <param name="question">The question and the scope bounding what may be read to answer it.</param>
    /// <param name="journal">Where the run publishes, which is what a client reads and reattaches to.</param>
    /// <param name="cancellationToken">Ends the run, which is what the longest a run may take is applied through.</param>
    /// <returns>A task that completes once the run has published its ending.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="question" /> or <paramref name="journal" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Cancellation ends the run as a stated failure rather than as a cancelled task, because the caller is a background
    /// worker with nowhere to report one and because a client watching a run that was stopped has to be told that rather
    /// than left on a connection that closes silently.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The run is executed after its request was answered, so an unhandled failure would be lost; it is published as the run's own stated ending instead.")]
    public async Task RunAsync(
        MailQuestion question,
        DiscoveryRunJournal journal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(journal);

        journal.Append(new DiscoveryRunStarted());

        try
        {
            var result = await this.run.RunAsync(
                question,
                progress => journal.Append(new DiscoveryRetrievalProgressed(progress)),
                cancellationToken);

            journal.Append(new DiscoveryRunCompleted(Present(result, journal)));
        }
        catch (OperationCanceledException)
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
        catch (Exception)
        {
            journal.Append(new DiscoveryRunFailed(DiscoveryRunFailure.Failed));
        }
    }

    /// <summary>Publishes what the run found, source by source and then block by block, and says what it could not publish.</summary>
    /// <remarks>
    /// A source refused by the stream's own bound takes the blocks resting on it with it, because a block naming a
    /// citation nobody declared is the one way a citation contract fails quietly. So the run stops at the first refusal
    /// and states that it composed less than it found, rather than publishing a block whose sources went missing.
    /// </remarks>
    private static List<PresentationLimitation> Present(DiscoveryRunResult result, DiscoveryRunJournal journal)
    {
        var citations = DiscoveryPresentationComposition.CitationsFor(result.Evidence);

        foreach (var citation in citations)
        {
            if (!journal.Append(new DiscoveryCitationDeclared(citation)))
            {
                return [PresentationLimitation.BlocksOmitted];
            }
        }

        foreach (var block in DiscoveryPresentationComposition.BlocksFor(result.Evidence, citations))
        {
            if (!journal.Append(new DiscoveryBlockComposed(block)))
            {
                return [PresentationLimitation.BlocksOmitted];
            }
        }

        return ReachOf(result);
    }

    /// <summary>Says what made the answer narrower than the question.</summary>
    /// <remarks>
    /// Two of the catalogue's members follow from what retrieval reported, and the rest do not. A run that stopped
    /// because it had found the passages its plan called enough left the rest of the matching mail unread, and a run
    /// whose ranking never reached meaning matched the question's own words alone. What a source's currency or its
    /// readability makes narrower is a judgement about the correspondence rather than about retrieval, and a run states
    /// nothing it did not establish.
    /// </remarks>
    private static List<PresentationLimitation> ReachOf(DiscoveryRunResult result)
    {
        List<PresentationLimitation> stated = [];

        if (result.Evidence.Passages.Count >= result.Plan.Retrieval.SufficientPassages)
        {
            stated.Add(PresentationLimitation.RetrievalTruncated);
        }

        if (result.Evidence.RetrievalMode is EmailSearchRetrievalMode.Lexical)
        {
            stated.Add(PresentationLimitation.SemanticRankingUnavailable);
        }

        return stated;
    }
}

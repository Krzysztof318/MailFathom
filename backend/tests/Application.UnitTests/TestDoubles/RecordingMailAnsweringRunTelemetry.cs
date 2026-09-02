// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Domain.Answering.Audit;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Stands in for the span a run is published as, keeping what the observation said when the scope was closed.</summary>
/// <remarks>
/// It snapshots rather than keeping the observation, because the observation is the run's own mutable record and a test
/// holding it would read whatever it happened to say afterwards. What the real adapter reads at that same moment is what
/// its tags carry, so a snapshot is the thing worth asserting here; the tag names are the adapter's own contract and are
/// proved against a real listener where that adapter lives.
/// </remarks>
internal sealed class RecordingMailAnsweringRunTelemetry : IMailAnsweringRunTelemetry
{
    /// <summary>Gets what the run said about itself when its report was closed, or <see langword="null" /> while none has been.</summary>
    public PublishedRun? Published { get; private set; }

    /// <summary>Gets how many reports were opened, so a refusal that reached no run can be told from a run that did nothing.</summary>
    public int OpenedCount { get; private set; }

    /// <inheritdoc />
    public IDisposable BeginRun(MailAnsweringRunObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        this.OpenedCount++;

        return new Report(this, observation);
    }

    /// <summary>What one run reported, as the report was closed.</summary>
    /// <param name="ChatEndpointAlias">This deployment's own configured name for the endpoint the run was conducted through.</param>
    /// <param name="InstructionsVersion">The version of the instruction the run was conducted under.</param>
    /// <param name="CandidateCount">How many candidates the run's lookups ranked.</param>
    /// <param name="RelevantCandidateCount">How many of those survived being judged.</param>
    /// <param name="PassageCount">How many passages reached the model.</param>
    /// <param name="Outcome">How the run ended.</param>
    /// <param name="Degradation">The ways the run read less than an undegraded run of the same question would.</param>
    internal sealed record PublishedRun(
        string ChatEndpointAlias,
        string InstructionsVersion,
        int CandidateCount,
        int RelevantCandidateCount,
        int PassageCount,
        MailAnsweringRunOutcome Outcome,
        MailAnsweringRunDegradation Degradation);

    private sealed class Report(
        RecordingMailAnsweringRunTelemetry telemetry,
        MailAnsweringRunObservation observation)
        : IDisposable
    {
        public void Dispose()
        {
            var retrieval = observation.Retrieval;

            telemetry.Published = new PublishedRun(
                observation.ChatEndpointAlias,
                observation.InstructionsVersion,
                retrieval.CandidateCount,
                retrieval.RelevantCandidateCount,
                retrieval.Passages.Count,
                observation.Outcome,
                retrieval.Degradation);
        }
    }
}

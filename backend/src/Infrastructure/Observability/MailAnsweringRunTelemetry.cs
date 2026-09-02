// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Publishes one answering run as a span beside the request it happened inside.</summary>
/// <remarks>
/// <para>
/// A span rather than a table, because everything it carries is a bounded fact about the run: how long it took, how many
/// candidates its lookups ranked, how many survived being judged, how many passages reached the model, how it ended, and
/// how it degraded. Which messages it read is the one part that cannot go here — a tag per message opens a time series
/// per person — and that part is the durable record's.
/// </para>
/// <para>
/// It is correlated with the MCP tool call by being started inside it. The SDK's own span for the call is the current
/// activity when a run begins, so this becomes its child and the provider calls the run makes become children of this,
/// which is what makes a slow run attributable without opening a database.
/// </para>
/// <para>
/// Nothing published here is a question, an answer, a query the model wrote, a retrieved passage, or a message
/// identifier. The endpoint alias is this deployment's own configured name; everything else is a count or one of a
/// bounded set of words.
/// </para>
/// </remarks>
public sealed class MailAnsweringRunTelemetry : IMailAnsweringRunTelemetry
{
    /// <summary>The name every answering run's span carries.</summary>
    /// <remarks>
    /// Named after the operation rather than after the tool, so the span reads as the work that was done and stays right
    /// if a second entrypoint ever asks the same question. The tool name is already on the SDK's span above it.
    /// </remarks>
    internal const string SpanName = "answer_mail_question";

    internal const string EndpointTagName = "mailfathom.answering.endpoint";

    internal const string InstructionsVersionTagName = "mailfathom.answering.instructions_version";

    internal const string CandidatesTagName = "mailfathom.answering.candidates";

    internal const string RelevantCandidatesTagName = "mailfathom.answering.candidates.relevant";

    internal const string PassagesTagName = "mailfathom.answering.passages";

    internal const string OutcomeTagName = "mailfathom.answering.outcome";

    internal const string DegradationTagName = "mailfathom.answering.degradation";

    /// <inheritdoc />
    public IDisposable BeginRun(MailAnsweringRunObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        return new RunSpan(Telemetry.ActivitySource.StartActivity(SpanName), observation);
    }

    /// <summary>Holds one run's span open and tags it with what the run turned out to have done.</summary>
    /// <remarks>
    /// The tags are attached on disposal rather than at the start, because every one of them is a fact the run
    /// establishes as it goes and several of them exist only once it has ended. The activity is null on an instance
    /// nothing is listening to, which is the ordinary case for a deployment exporting nothing, and the scope then costs
    /// one allocation and no work.
    /// </remarks>
    private sealed class RunSpan(Activity? activity, MailAnsweringRunObservation observation) : IDisposable
    {
        public void Dispose()
        {
            if (activity is null)
            {
                return;
            }

            var retrieval = observation.Retrieval;

            activity.SetTag(EndpointTagName, observation.ChatEndpointAlias);
            activity.SetTag(InstructionsVersionTagName, observation.InstructionsVersion);
            activity.SetTag(CandidatesTagName, retrieval.CandidateCount);
            activity.SetTag(RelevantCandidatesTagName, retrieval.RelevantCandidateCount);
            activity.SetTag(PassagesTagName, retrieval.Passages.Count);
            activity.SetTag(OutcomeTagName, observation.Outcome.ToString());
            activity.SetTag(DegradationTagName, retrieval.Degradation.ToString());

            activity.Dispose();
        }
    }
}

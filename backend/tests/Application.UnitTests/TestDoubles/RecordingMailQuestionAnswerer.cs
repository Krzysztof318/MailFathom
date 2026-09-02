// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Domain.Answering.Audit;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>An answering port that records what a run was asked and answers from a script.</summary>
/// <remarks>
/// It stands in for the agent rather than for the provider, because what the use case above it owns is the question, the
/// scope, and what one response publishes — none of which a real run would make more observable. Every question it
/// received is kept, so a test can assert that a refused request reached no run at all.
/// </remarks>
internal sealed class RecordingMailQuestionAnswerer : IMailQuestionAnswerer
{
    /// <summary>The endpoint alias every scripted run reports having been conducted through.</summary>
    internal const string EndpointAlias = "scripted-endpoint";

    /// <summary>The instruction version every scripted run reports having been conducted under.</summary>
    internal const string InstructionsVersion = "scripted-instructions";

    private readonly List<MailQuestion> questions = [];
    private MailAnswer answer = new("an answer");
    private MailAnsweringRetrievalReport retrieval = MailAnsweringRetrievalReport.Empty;
    private Exception? failure;

    /// <summary>Gets every question this answerer was asked, in the order it was asked them.</summary>
    public IReadOnlyList<MailQuestion> Questions => this.questions;

    /// <summary>Scripts what the next run answers with, and the mail it reports having retrieved.</summary>
    /// <param name="text">The answer text.</param>
    /// <param name="passages">The passages the run is to report having retrieved.</param>
    /// <returns>The same answerer, so a test arranges it in one expression.</returns>
    public RecordingMailQuestionAnswerer Answering(string text, params EmailKnowledgePassage[] passages)
    {
        this.answer = new MailAnswer(text);
        this.retrieval = this.retrieval with
        {
            Passages = passages,
            CandidateCount = passages.Length,
            RelevantCandidateCount = passages.Length,
        };

        return this;
    }

    /// <summary>Scripts the next run as one that reached this deployment's ceiling on retrieved mail.</summary>
    /// <returns>The same answerer, so a test arranges it in one expression.</returns>
    public RecordingMailQuestionAnswerer HavingReachedTheRetrievalCeiling() =>
        this.Degraded(MailAnsweringRunDegradation.RetrievalCeilingReached);

    /// <summary>Scripts the next run as one whose relevance filter could not judge what it found.</summary>
    /// <returns>The same answerer, so a test arranges it in one expression.</returns>
    public RecordingMailQuestionAnswerer HavingFallenBackToTheUnjudgedRanking() =>
        this.Degraded(MailAnsweringRunDegradation.RelevanceFilterFellBack);

    /// <summary>Scripts the next run as one that ends without an answer.</summary>
    /// <param name="ending">What ends it.</param>
    /// <returns>The same answerer, so a test arranges it in one expression.</returns>
    /// <remarks>
    /// The retrieval scripted beside it is still reported, which is what a test of a failed run's record asserts: a run
    /// that failed after reading mail has read it.
    /// </remarks>
    public RecordingMailQuestionAnswerer Failing(Exception ending)
    {
        this.failure = ending;

        return this;
    }

    /// <inheritdoc />
    public Task<MailAnswer> AnswerAsync(
        MailQuestion question,
        MailAnsweringRunObservation observation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(observation);

        this.questions.Add(question);

        observation.RecordComposition(EndpointAlias, InstructionsVersion);
        observation.RecordRetrieval(this.retrieval);

        return this.failure is { } ending ? Task.FromException<MailAnswer>(ending) : Task.FromResult(this.answer);
    }

    private RecordingMailQuestionAnswerer Degraded(MailAnsweringRunDegradation degradation)
    {
        this.retrieval = this.retrieval with { Degradation = this.retrieval.Degradation | degradation };

        return this;
    }
}

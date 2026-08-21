// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Domain.Answering.Audit;

namespace MailFathom.Mcp.UnitTests.TestDoubles;

/// <summary>An answering port that records what a run was asked and answers from a script.</summary>
/// <remarks>
/// The boundary below the use case, stubbed for the reason the lexical index is in the search tests: what this suite is
/// about is the arguments the tool converts and the answer it publishes, and a real run would put a provider between the
/// two without making either more observable.
/// </remarks>
internal sealed class StubMailQuestionAnswerer : IMailQuestionAnswerer
{
    private MailAnswer answer = new("an answer");
    private MailAnsweringRetrievalReport retrieval = MailAnsweringRetrievalReport.Empty;

    /// <summary>Gets the question the last run was asked, or <see langword="null" /> while no run has been asked one.</summary>
    public MailQuestion? LastQuestion { get; private set; }

    /// <summary>Scripts what the next run answers with, and the mail it reports having retrieved.</summary>
    /// <param name="text">The answer text.</param>
    /// <param name="passages">The passages the run is to report having retrieved.</param>
    /// <returns>The same answerer, so a test arranges it in one expression.</returns>
    public StubMailQuestionAnswerer Answering(string text, params EmailKnowledgePassage[] passages)
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
    public StubMailQuestionAnswerer HavingReachedTheRetrievalCeiling()
    {
        this.retrieval = this.retrieval with
        {
            Degradation = this.retrieval.Degradation | MailAnsweringRunDegradation.RetrievalCeilingReached,
        };

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

        this.LastQuestion = question;

        observation.RecordComposition("stub-endpoint", "stub-instructions");
        observation.RecordRetrieval(this.retrieval);

        return Task.FromResult(this.answer);
    }
}

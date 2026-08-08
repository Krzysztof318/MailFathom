// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Retrieval;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>An answering port that records what a run was asked and answers from a script.</summary>
/// <remarks>
/// It stands in for the agent rather than for the provider, because what the use case above it owns is the question, the
/// scope, and what one response publishes — none of which a real run would make more observable. Every question it
/// received is kept, so a test can assert that a refused request reached no run at all.
/// </remarks>
internal sealed class RecordingMailQuestionAnswerer : IMailQuestionAnswerer
{
    private readonly List<MailQuestion> questions = [];
    private MailAnswer answer = new("an answer", [], RetrievalWasTruncated: false);

    /// <summary>Gets every question this answerer was asked, in the order it was asked them.</summary>
    public IReadOnlyList<MailQuestion> Questions => this.questions;

    /// <summary>Scripts what the next run answers with.</summary>
    /// <param name="text">The answer text.</param>
    /// <param name="passages">The passages the run is to report having retrieved.</param>
    /// <returns>The same answerer, so a test arranges it in one expression.</returns>
    public RecordingMailQuestionAnswerer Answering(string text, params EmailKnowledgePassage[] passages)
    {
        this.answer = new MailAnswer(text, passages, RetrievalWasTruncated: false);

        return this;
    }

    /// <summary>Scripts the next run as one that reached this deployment's ceiling on retrieved mail.</summary>
    /// <returns>The same answerer, so a test arranges it in one expression.</returns>
    public RecordingMailQuestionAnswerer HavingReachedTheRetrievalCeiling()
    {
        this.answer = this.answer with { RetrievalWasTruncated = true };

        return this;
    }

    /// <inheritdoc />
    public Task<MailAnswer> AnswerAsync(MailQuestion question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);

        this.questions.Add(question);

        return Task.FromResult(this.answer);
    }
}

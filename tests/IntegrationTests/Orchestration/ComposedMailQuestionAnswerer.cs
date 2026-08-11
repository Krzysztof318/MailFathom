// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.AI.Retrieval;
using MailFathom.Application.Chat;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Conducts an answering run over the deployment's own composition and a scripted provider.</summary>
/// <remarks>
/// <para>
/// The production answerer opens a credential, a transport, and a provider client per run, so reaching it from a test
/// would mean reaching a network. This stands exactly where it stands and does everything it does with those three
/// removed: the same agent composition, the same run ledger, the same scoped retrieval, and the same reading of an
/// empty answer. What it is not is a second implementation of the run — every one of those pieces is resolved from the
/// assemblies that ship.
/// </para>
/// <para>
/// It reports what the run retrieved however the run ended, for the reason the production answerer does: a run that
/// failed part way through has already read somebody's mail, and a record built from the answer alone would say it read
/// nothing.
/// </para>
/// </remarks>
internal sealed class ComposedMailQuestionAnswerer(
    IEmailKnowledgeSearch knowledgeSearch,
    MailAnsweringRunBounds runBounds,
    ChatGenerationPlan plan,
    IChatClient chatClient) : IMailQuestionAnswerer
{
    /// <inheritdoc />
    public async Task<MailAnswer> AnswerAsync(
        MailQuestion question,
        MailAnsweringRunObservation observation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(observation);

        observation.RecordComposition(plan.Endpoint.Alias, MailAnsweringInstructions.Version);

        var runLedger = new MailAnsweringRunLedger(runBounds);
        var retrieval = new ScopedMailKnowledgeRetrieval(knowledgeSearch, question.Scope, runLedger);

        try
        {
            var agent = MailAnsweringAgentComposition.Compose(
                chatClient,
                plan,
                retrieval,
                NullLoggerFactory.Instance);

            var response = await agent.RunAsync(
                question.Text.Value,
                session: null,
                options: null,
                cancellationToken);

            return string.IsNullOrWhiteSpace(response.Text)
                ? throw new ChatGenerationFailedException(plan.Endpoint.Alias, ChatGenerationFailure.AnswerEmpty)
                : new MailAnswer(response.Text);
        }
        finally
        {
            observation.RecordRetrieval(retrieval.Report);
        }
    }
}

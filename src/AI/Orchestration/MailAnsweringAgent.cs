// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.Retrieval;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Domain.Answering.Audit;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.Orchestration;

/// <summary>Answers one question by running the composed agent against the declared chat endpoint.</summary>
/// <remarks>
/// <para>
/// The boundary between the application's question and an orchestration framework. Everything the framework publishes —
/// the agent, its session, its messages, its context providers, its tools — stops here, and what leaves is an answer and
/// the passages the run retrieved.
/// </para>
/// <para>
/// Each run opens its own credential, transport, chat client, and agent, and releases all four with the run. That is the
/// same lifetime the single-request chat adapter uses and for the same reasons: a rotated key is picked up by the next
/// question rather than at the next restart, and one caller's retrieved mail cannot outlive the call that retrieved it.
/// </para>
/// </remarks>
internal sealed class MailAnsweringAgent : IMailQuestionAnswerer
{
    private readonly ChatGenerationPlan plan;
    private readonly MailAnsweringRunBounds runBounds;
    private readonly IProviderEndpointCredentialSource credentialSource;
    private readonly OpenAiCompatibleClientFactory clientFactory;
    private readonly IHttpClientFactory transportFactory;
    private readonly IEmailKnowledgeSearch knowledgeSearch;
    private readonly IOutboundOperationRunner operationRunner;
    private readonly IAiProviderHealthRecorder healthRecorder;
    private readonly IMailAnsweringSpendLedger spendLedger;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<MailAnsweringAgent> logger;

    /// <summary>Initializes the answering agent over the declared endpoint, its credentials, its transport, and this deployment's retrieval.</summary>
    /// <param name="plan">The validated declaration: which endpoint answers and with which parameters.</param>
    /// <param name="runBounds">What one run may send, call, and consume before it is stopped.</param>
    /// <param name="credentialSource">Resolves what a request presents to the endpoint.</param>
    /// <param name="clientFactory">Opens a provider client over the endpoint.</param>
    /// <param name="transportFactory">Opens the transport a run's requests are sent over.</param>
    /// <param name="knowledgeSearch">Finds the mail a run retrieves, within the scope the question carries.</param>
    /// <param name="operationRunner">Applies the provider resilience budget to every call the run makes.</param>
    /// <param name="healthRecorder">Records what each call established about the provider.</param>
    /// <param name="spendLedger">Counts what every call of this run added to the current period.</param>
    /// <param name="loggerFactory">Creates the loggers the framework's own components record through.</param>
    /// <param name="logger">Records the outcome without recording any question, answer, or passage.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailAnsweringAgent(
        ChatGenerationPlan plan,
        MailAnsweringRunBounds runBounds,
        IProviderEndpointCredentialSource credentialSource,
        OpenAiCompatibleClientFactory clientFactory,
        IHttpClientFactory transportFactory,
        IEmailKnowledgeSearch knowledgeSearch,
        IOutboundOperationRunner operationRunner,
        IAiProviderHealthRecorder healthRecorder,
        IMailAnsweringSpendLedger spendLedger,
        ILoggerFactory loggerFactory,
        ILogger<MailAnsweringAgent> logger)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(runBounds);
        ArgumentNullException.ThrowIfNull(credentialSource);
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(transportFactory);
        ArgumentNullException.ThrowIfNull(knowledgeSearch);
        ArgumentNullException.ThrowIfNull(operationRunner);
        ArgumentNullException.ThrowIfNull(healthRecorder);
        ArgumentNullException.ThrowIfNull(spendLedger);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(logger);

        this.plan = plan;
        this.runBounds = runBounds;
        this.credentialSource = credentialSource;
        this.clientFactory = clientFactory;
        this.transportFactory = transportFactory;
        this.knowledgeSearch = knowledgeSearch;
        this.operationRunner = operationRunner;
        this.healthRecorder = healthRecorder;
        this.spendLedger = spendLedger;
        this.loggerFactory = loggerFactory;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<MailAnswer> AnswerAsync(
        MailQuestion question,
        MailAnsweringRunObservation observation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(observation);

        var endpoint = this.plan.Endpoint;

        // Recorded before anything that can fail, so a run stopped on its first call is still attributable to the
        // profile and the policy that produced it rather than leaving both blank in its own record.
        observation.RecordComposition(endpoint.Alias, MailAnsweringInstructions.Version);

        // The question is one turn, so the bound on what one call may carry is the bound on the question. What the
        // retrieval adds beside it is bounded where the passages are built, and the run's instruction is a constant of
        // this build rather than anything a caller composes.
        ChatRequestBounds.Require(
            [new ChatMessage(ChatRole.User, question.Text.Value)],
            this.plan.MaximumMessagesPerRequest,
            this.plan.MaximumRequestCharacters);

        var runLedger = new MailAnsweringRunLedger(this.runBounds);
        var retrieval = new ScopedMailKnowledgeRetrieval(this.knowledgeSearch, question.Scope, runLedger);

        try
        {
            return await this.ConductAsync(question, endpoint, retrieval, runLedger, cancellationToken);
        }
        finally
        {
            // Whatever ended the run, what it retrieved before that is what its record has to state: a run that failed
            // on its third provider call has already read somebody's mail, and reporting only on the way out through
            // an answer would say it read nothing.
            observation.RecordRetrieval(retrieval.Report);
        }
    }

    /// <summary>Opens the run's own credential, transport, client, and agent, and answers through them.</summary>
    /// <remarks>All four are released with the run, which is the same lifetime the single-request chat adapter uses and for the same reasons.</remarks>
    private async Task<MailAnswer> ConductAsync(
        MailQuestion question,
        ChatEndpoint endpoint,
        ScopedMailKnowledgeRetrieval retrieval,
        MailAnsweringRunLedger runLedger,
        CancellationToken cancellationToken)
    {
        // Resolved per run and released with it, so a rotated key is picked up by the next question and the material
        // exists for one run rather than for process uptime.
        using var credential = await this.credentialSource.ResolveAsync(endpoint.Alias, cancellationToken);
        using var transport = this.transportFactory.CreateClient(ProviderChatModelClient.TransportName);
        using var providerClient = this.clientFactory.OpenChatClient(endpoint, credential, transport);
        using var resilientClient = new ResilientChatClient(
            providerClient,
            endpoint,
            this.plan.RequestTimeout,
            this.operationRunner,
            this.healthRecorder,
            this.loggerFactory.CreateLogger<ResilientChatClient>());

        // Outside the resilience decorator rather than inside it, so a call this deployment's own ceiling refused never
        // reaches the endpoint's circuit, its concurrency budget, or its health record.
        using var chatClient = new BudgetedChatClient(resilientClient, runLedger, this.spendLedger);

        var agent = MailAnsweringAgentComposition.Compose(chatClient, this.plan, retrieval, this.loggerFactory);
        var response = await agent.RunAsync(question.Text.Value, session: null, options: null, cancellationToken);
        var report = retrieval.Report;

        if (string.IsNullOrWhiteSpace(response.Text))
        {
            // Logged before the failure is raised, because the failure names only that no text arrived, while the count
            // says whether the run had anything to answer from.
            MailAnsweringEvents.LogRunProducedNoAnswer(this.logger, endpoint.Alias, report.Passages.Count);

            throw new ChatGenerationFailedException(endpoint.Alias, ChatGenerationFailure.AnswerEmpty);
        }

        // Smaller than the passage count wherever one message answered two of the model's queries, which is what makes
        // the pair worth recording: it says how much of the mailbox one question actually reached.
        var emailCount = report.Passages.DistinctBy(static passage => passage.StoredEmailId).Count();

        MailAnsweringEvents.LogAnswered(this.logger, endpoint.Alias, report.Passages.Count, emailCount);

        if (report.Degradation.HasFlag(MailAnsweringRunDegradation.RetrievalCeilingReached))
        {
            MailAnsweringEvents.LogRetrievalCeilingReached(this.logger, endpoint.Alias, report.Passages.Count);
        }

        return new MailAnswer(response.Text);
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Emails;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.AgentConversations;

/// <summary>Summarises the earlier part of an Agent conversation through an agent of its own, under the model the deployment names for compaction.</summary>
/// <remarks>
/// <para>
/// One call, no tools. What leaves this deployment is text the conversation already sent on an earlier turn, guarded
/// again because a person may have pasted anything into it, and it is spent through a run ceiling of its own and the
/// deployment's period like every other call.
/// </para>
/// <para>
/// A model that fails, times out, or answers with nothing is not the turn's failure: the answer is <see langword="null" />,
/// the turn is composed from as much recent history as fits, and the line logged here is what tells an operator that
/// compaction is not happening. A spend ceiling is the exception, and propagates, because the turn itself would be
/// refused by the same ledger.
/// </para>
/// </remarks>
internal sealed class AgentConversationSummarizer : IAgentConversationSummarizer
{
    private readonly ChatGenerationPlan plan;
    private readonly MailAnsweringRunBounds runBounds;
    private readonly IMailAnsweringSpendLedger spendLedger;
    private readonly IProviderEndpointCredentialSource credentialSource;
    private readonly OpenAiCompatibleClientFactory clientFactory;
    private readonly IHttpClientFactory transportFactory;
    private readonly IOutboundOperationRunner operationRunner;
    private readonly IAiProviderHealthRecorder healthRecorder;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly IAgentInstructionEnvelope instructionEnvelope;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<AgentConversationSummarizer> logger;

    /// <summary>Initializes the summariser over the model declared for compaction.</summary>
    /// <param name="plan">The validated declaration: which endpoint summarises and with which parameters.</param>
    /// <param name="runBounds">What one summary may send, call, and consume before it is stopped.</param>
    /// <param name="spendLedger">Counts what the call added to the current period.</param>
    /// <param name="credentialSource">Resolves what a request presents to the endpoint.</param>
    /// <param name="clientFactory">Opens a provider client over the endpoint.</param>
    /// <param name="transportFactory">Opens the transport the request is sent over.</param>
    /// <param name="operationRunner">Applies the provider resilience budget to the call.</param>
    /// <param name="healthRecorder">Records what the call established about the provider.</param>
    /// <param name="egressGuard">Scans the text sent to the provider.</param>
    /// <param name="instructionEnvelope">Supplies the text every composed agent's instruction is wrapped in.</param>
    /// <param name="loggerFactory">Creates the loggers the framework's own components record through.</param>
    /// <param name="logger">Records the outcome without recording any part of the conversation.</param>
    public AgentConversationSummarizer(
        [FromKeyedServices(ChatCapability.ConversationCompaction)] ChatGenerationPlan plan,
        MailAnsweringRunBounds runBounds,
        IMailAnsweringSpendLedger spendLedger,
        IProviderEndpointCredentialSource credentialSource,
        OpenAiCompatibleClientFactory clientFactory,
        IHttpClientFactory transportFactory,
        IOutboundOperationRunner operationRunner,
        IAiProviderHealthRecorder healthRecorder,
        SensitiveContentEgressGuard egressGuard,
        IAgentInstructionEnvelope instructionEnvelope,
        ILoggerFactory loggerFactory,
        ILogger<AgentConversationSummarizer> logger)
    {
        this.plan = plan;
        this.runBounds = runBounds;
        this.spendLedger = spendLedger;
        this.credentialSource = credentialSource;
        this.clientFactory = clientFactory;
        this.transportFactory = transportFactory;
        this.operationRunner = operationRunner;
        this.healthRecorder = healthRecorder;
        this.egressGuard = egressGuard;
        this.instructionEnvelope = instructionEnvelope;
        this.loggerFactory = loggerFactory;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> SummarizeAsync(
        string? previousSummary,
        IReadOnlyList<AgentHistoryTurn> turns,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turns);

        var turn = await this.egressGuard.GuardAsync(
            SensitiveContentEgressPoint.ChatPrompt,
            AgentConversationSummaryInstructions.ComposeTurn(previousSummary, turns),
            cancellationToken);
        var runLedger = new MailAnsweringRunLedger(this.runBounds);

        try
        {
            return await ChatModelFallThrough.RunAsync(
                this.plan,
                this.logger,
                (model, attemptToken) => this.AskAsync(model, turn, runLedger, attemptToken),
                cancellationToken);
        }
        catch (ChatGenerationFailedException failure)
        {
            AgentConversationEvents.LogCompactionFailed(this.logger, failure.EndpointAlias, failure.Failure);

            return null;
        }
    }

    private async Task<string> AskAsync(
        ChatGenerationPlan model,
        string turn,
        MailAnsweringRunLedger runLedger,
        CancellationToken cancellationToken)
    {
        ChatRequestBounds.RequireForAttempt([new ChatMessage(ChatRole.User, turn)], model);

        var endpoint = model.Endpoint;

        using var credential = await ChatModelCredential.ResolveAsync(this.credentialSource, endpoint, cancellationToken);
        using var transport = this.transportFactory.CreateClient(ProviderChatModelClient.TransportName);
        using var providerClient = this.clientFactory.OpenChatClient(endpoint, credential, transport);
        using var resilientClient = new ResilientChatClient(
            providerClient,
            endpoint,
            model.RequestTimeout,
            this.operationRunner,
            this.healthRecorder,
            this.loggerFactory.CreateLogger<ResilientChatClient>());
        await using var budgetedClient = new BudgetedChatClient(resilientClient, runLedger, this.spendLedger);

        var agent = AgentConversationSummaryComposition.Compose(budgetedClient, model, this.instructionEnvelope, this.loggerFactory);
        var response = await agent.RunAsync(turn, session: null, options: null, cancellationToken);
        var summary = SummaryOf(response.Text)
            ?? throw new ChatGenerationFailedException(endpoint.Alias, ChatGenerationFailure.AnswerEmpty);

        AgentConversationEvents.LogCompacted(this.logger, endpoint.Alias, summary.Length);

        return summary;
    }

    /// <summary>Reads what the model wrote as the summary a compaction keeps, or as none where it wrote nothing.</summary>
    /// <param name="text">The text the model's turn carried.</param>
    /// <returns>
    /// The summary, trimmed and cut to what a compaction may keep, or <see langword="null" /> where nothing is left of it,
    /// which a compaction fails as an empty answer.
    /// </returns>
    internal static string? SummaryOf(string? text)
    {
        var summary = text?.Trim();

        if (string.IsNullOrEmpty(summary))
        {
            return null;
        }

        return summary.Length <= AgentConversationSummaryInstructions.MaximumSummaryLength
            ? summary
            : MailTextBounds.TruncateAtTextElementBoundary(summary, AgentConversationSummaryInstructions.MaximumSummaryLength);
    }
}

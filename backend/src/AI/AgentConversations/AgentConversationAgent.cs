// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.Retrieval;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.AgentConversations;

/// <summary>Composes the Agent's answer to one question as an Agent Framework agent over the person's mail, calendar, and tasks.</summary>
/// <remarks>
/// <para>
/// The composition is <see cref="AgentComposition" />'s, like every other AI operation here; what is this agent's own is
/// its instruction and its tools, which <see cref="AgentConversationTools" /> states. Every call goes through the run's
/// own ceiling and the provider's bulkhead, in that order, exactly as the answering and drafting agents send theirs.
/// </para>
/// <para>
/// <strong>What it composes is written as it is composed.</strong> A status line before each tool, the state of a
/// conversation and every proposal the moment a tool produces it, and the answer itself once the model has said it. None
/// of it waits for the run to end, which is what lets a client that was never connected read the whole of it later.
/// </para>
/// <para>
/// The answer cites every message the run read rather than the ones the model says it used. A citation the model named
/// would be a claim about its own reading; the ones minted here are what it was actually shown.
/// </para>
/// </remarks>
internal sealed class AgentConversationAgent : IAgentAnswerComposer
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
    private readonly AgentConversationReaders readers;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<AgentConversationAgent> logger;

    /// <summary>Initializes the agent over the declared endpoint and the use cases its tools read and propose through.</summary>
    /// <param name="plan">The validated declaration: which endpoint answers and with which parameters.</param>
    /// <param name="runBounds">What one run may send, call, and consume before it is stopped.</param>
    /// <param name="spendLedger">Counts what every call of the run added to the current period.</param>
    /// <param name="credentialSource">Resolves what a request presents to the endpoint.</param>
    /// <param name="clientFactory">Opens a provider client over the endpoint.</param>
    /// <param name="transportFactory">Opens the transport a run's requests are sent over.</param>
    /// <param name="operationRunner">Applies the provider resilience budget to every call.</param>
    /// <param name="healthRecorder">Records what each call established about the provider.</param>
    /// <param name="egressGuard">Scans every text sent to the provider.</param>
    /// <param name="instructionEnvelope">Supplies the text every composed agent's instruction is wrapped in.</param>
    /// <param name="readers">The use cases the tools read and propose through.</param>
    /// <param name="loggerFactory">Creates the loggers the framework's own components record through.</param>
    /// <param name="logger">Records the outcome without recording any question, answer, or message.</param>
    public AgentConversationAgent(
        [FromKeyedServices(ChatCapability.Agent)] ChatGenerationPlan plan,
        MailAnsweringRunBounds runBounds,
        IMailAnsweringSpendLedger spendLedger,
        IProviderEndpointCredentialSource credentialSource,
        OpenAiCompatibleClientFactory clientFactory,
        IHttpClientFactory transportFactory,
        IOutboundOperationRunner operationRunner,
        IAiProviderHealthRecorder healthRecorder,
        SensitiveContentEgressGuard egressGuard,
        IAgentInstructionEnvelope instructionEnvelope,
        AgentConversationReaders readers,
        ILoggerFactory loggerFactory,
        ILogger<AgentConversationAgent> logger)
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
        this.readers = readers;
        this.loggerFactory = loggerFactory;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PresentationText>> ComposeAsync(AgentAnswerBrief brief, AgentAnswerJournal journal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brief);
        ArgumentNullException.ThrowIfNull(journal);

        var scope = this.readers.ScopeResolver.ReadableScope([], [], JunkMailInclusion.Excluded);
        var runLedger = new MailAnsweringRunLedger(this.runBounds);
        var retrieval = new ScopedMailKnowledgeRetrieval(
            this.readers.KnowledgeSearch,
            scope,
            runLedger,
            this.egressGuard,
            brief.Question.AskedAt);
        var tools = new AgentConversationTools(journal, retrieval, this.readers, this.egressGuard, scope.AccountIds);
        var messages = ComposeMessages(brief, scope);

        var answer = await ChatModelFallThrough.RunAsync(
            this.plan,
            this.logger,
            (model, attemptToken) => this.AskAsync(model, brief.Language, messages, tools, journal, runLedger, attemptToken),
            () => !tools.HasProposed,
            cancellationToken);

        await tools.DeclareSearchedAsync(cancellationToken);

        if (!await journal.ReportAsync(AgentActivity.ComposingAnswer, cancellationToken))
        {
            journal.Stopping.ThrowIfCancellationRequested();
        }

        var cited = tools.Cited.Take(PresentationEvidence.MaxCitations).Select(static citation => citation.Id).ToArray();
        var evidence = cited.Length is 0
            ? PresentationEvidence.Unsupported(PresentationFreshness.Unknown)
            : new PresentationEvidence(PresentationSupport.Supported, cited, PresentationFreshness.Unknown);

        await journal.ComposeAsync(new AnswerBlock(evidence, answer, PresentationConfidence.Moderate), cancellationToken);

        AgentConversationEvents.LogAnswered(this.logger, this.plan.Endpoint.Alias, cited.Length);

        return tools.FollowUps;
    }

    /// <summary>Turns an answer into the text a block may carry, or refuses it as no answer at all.</summary>
    private static PresentationText? Presentable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = new string([.. text.Where(static character => !char.IsControl(character) || character is '\n' or '\r' or '\t')]).Trim();
        var bounded = cleaned.Length <= PresentationText.MaxLength
            ? cleaned
            : MailTextBounds.TruncateAtTextElementBoundary(cleaned, PresentationText.MaxLength);

        return PresentationText.TryCreate(bounded, out var presentable) ? presentable : null;
    }

    /// <summary>Composes what the model is sent: every earlier turn of the conversation, then the question's own turn.</summary>
    /// <param name="brief">The question, the language it is answered in, and the conversation before it.</param>
    /// <param name="scope">The mailbox the run reads, whose accounts the turn names.</param>
    /// <returns>The conversation, oldest turn first.</returns>
    internal static IReadOnlyList<ChatMessage> ComposeMessages(AgentAnswerBrief brief, MailboxScope scope) =>
    [
        .. brief.History.Select(static turn => new ChatMessage(
            turn.Author is AgentMessageAuthor.Person ? ChatRole.User : ChatRole.Assistant,
            turn.Text)),
        new ChatMessage(
            ChatRole.User,
            AgentConversationComposition.ComposeTurn(brief.Question.AskedAt, scope.AccountIds, brief.Question.Scope, brief.Question.Text.Value)),
    ];

    /// <summary>Scans every turn the person or the Agent wrote, which is what leaves this deployment on each call.</summary>
    private async Task<IReadOnlyList<ChatMessage>> GuardAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        var guarded = await this.egressGuard.GuardAllAsync(
            SensitiveContentEgressPoint.ChatPrompt,
            [.. messages.Select(static message => message.Text)],
            cancellationToken);

        return [.. messages.Select((message, index) => new ChatMessage(message.Role, guarded[index]))];
    }

    private async Task<PresentationText> AskAsync(
        ChatGenerationPlan model,
        UserLanguage language,
        IReadOnlyList<ChatMessage> messages,
        AgentConversationTools tools,
        AgentAnswerJournal journal,
        MailAnsweringRunLedger runLedger,
        CancellationToken cancellationToken)
    {
        // Against this model's own bounds, and before the scan, so a conversation refused by a ceiling costs no scan.
        ChatRequestBounds.RequireForAttempt(messages, model);
        tools.ForgetFollowUps();

        var guarded = await this.GuardAsync(messages, cancellationToken);

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
        using var recordedClient = new RecordedChatClient(budgetedClient, journal);
        using var steeredClient = new SteeredChatClient(recordedClient, journal, this.egressGuard);

        var agent = AgentConversationComposition.Compose(steeredClient, model, language, tools.Create(), this.instructionEnvelope, this.loggerFactory);
        var response = await agent.RunAsync(ChatConversationMapping.ToProviderConversation(guarded), session: null, options: null, cancellationToken);

        if (Presentable(response.Text) is not { } answer)
        {
            AgentConversationEvents.LogProducedNoAnswer(this.logger, endpoint.Alias);

            throw new ChatGenerationFailedException(endpoint.Alias, ChatGenerationFailure.AnswerEmpty);
        }

        return answer;
    }
}

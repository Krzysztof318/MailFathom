// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.Tasks;
using MailFathom.Domain.Emails;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.DayLayout;

/// <summary>Arranges one day by putting it to the composed day-layout agent.</summary>
/// <remarks>
/// <para>
/// One call, no tools, and one day. What leaves this deployment is the window, the lines of the tasks owed by the end
/// of it, and the titles and times of what is already committed during it, every one of them guarded first — so an
/// arrangement sends the day and nothing about the lists it was read from.
/// </para>
/// <para>
/// It opens no chat call of its own. The agent is composed by <see cref="AgentComposition" /> like every other
/// operation in this product, so the instruction is carried where no turn can reach it, the tool set is the capability,
/// and the registered instruction envelope is wrapped around the instruction rather than written into it.
/// </para>
/// <para>
/// Every arrangement is admitted against the same period ceilings a question is, and every call is counted by the same
/// two ledgers. It is not a new category of spend: this runs when somebody presses a control, so what it costs a
/// deployment is what its people asked for, and a refused admission withholds the arrangement rather than failing it —
/// the day is unchanged and the control may be pressed again.
/// </para>
/// <para>
/// A provider that fails, times out, or is unreachable withholds the arrangement for the same reason. A provider that
/// <em>answered</em> with something unreadable does not: the call was made and paid for, and repeating it buys the same
/// answer, so the day is answered with an arrangement of nothing rather than with a failure the person cannot act on.
/// </para>
/// <para>
/// Each arrangement opens its own credential, transport, chat client, and agent, and releases all four with it. That is
/// the same lifetime every other agent here uses and for the same reasons: a rotated key is picked up by the next
/// request rather than at the next restart, and one person's day cannot outlive the call that sent it.
/// </para>
/// </remarks>
internal sealed class DayLayoutAgent : IDayLayoutPlanner
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
    private readonly ILogger<DayLayoutAgent> logger;

    /// <summary>Initializes the arrangement over the declared endpoint, its credentials, and its transport.</summary>
    /// <param name="plan">The validated declaration: which endpoint arranges and with which parameters.</param>
    /// <param name="runBounds">What one arrangement may send, call, and consume before it is stopped.</param>
    /// <param name="spendLedger">Admits one arrangement against the current period and counts what its call consumed.</param>
    /// <param name="credentialSource">Resolves the endpoint's credential for the one call.</param>
    /// <param name="clientFactory">Opens the provider client.</param>
    /// <param name="transportFactory">Supplies the named outbound transport that client speaks over.</param>
    /// <param name="operationRunner">Applies this deployment's outbound resilience to the call.</param>
    /// <param name="healthRecorder">Records what the endpoint did, so the availability gate can read it.</param>
    /// <param name="egressGuard">Withholds from the provider whatever this deployment withholds.</param>
    /// <param name="instructionEnvelope">The preamble and postamble every agent here carries.</param>
    /// <param name="loggerFactory">The factory the composed agent and the resilience decorator log through.</param>
    /// <param name="logger">Records the outcome without recording the day or what was arranged.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public DayLayoutAgent(
        [FromKeyedServices(ChatCapability.DayLayout)] ChatGenerationPlan plan,
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
        ILogger<DayLayoutAgent> logger)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(runBounds);
        ArgumentNullException.ThrowIfNull(spendLedger);
        ArgumentNullException.ThrowIfNull(credentialSource);
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(transportFactory);
        ArgumentNullException.ThrowIfNull(operationRunner);
        ArgumentNullException.ThrowIfNull(healthRecorder);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(instructionEnvelope);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(logger);

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
    /// <remarks>This one is registered only where a chat endpoint was declared, so it is active by construction.</remarks>
    public bool IsActive => true;

    /// <inheritdoc />
    public async Task<DayLayoutDerivation> SuggestAsync(
        DayLayoutQuestion question,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);

        // A day with nothing owed is arranged without asking anybody: the only answer is two empty lists, and a call
        // to produce it would spend a person's allowance on a question that has one answer.
        if (question.Tasks.Count is 0)
        {
            return DayLayoutDerivation.Settled(new DayLayoutSuggestion([], []));
        }

        // Admitted before anything is composed or scanned, so a period this deployment has already spent costs nothing
        // to refuse.
        if (!await this.spendLedger.TryAdmitRunAsync(cancellationToken))
        {
            return this.Withhold(DayLayoutWithholding.AllowanceExhausted);
        }

        // A task's line and an appointment's title are somebody's own text and some of it was read out of their mail,
        // so both are scanned like every other text this deployment sends.
        var taskTitles = await this.egressGuard.GuardAllAsync(
            SensitiveContentEgressPoint.ChatPrompt,
            [.. question.Tasks.Select(static task => task.Title)],
            cancellationToken);
        var commitmentTitles = await this.egressGuard.GuardAllAsync(
            SensitiveContentEgressPoint.ChatPrompt,
            [.. question.Commitments.Select(static commitment => commitment.Title)],
            cancellationToken);

        var turn = Bounded(
            DayLayoutInstructions.ComposeLayoutTurn(question, taskTitles, commitmentTitles),
            this.plan.MaximumRequestCharacters);

        ChatRequestBounds.Require(
            [new ChatMessage(ChatRole.User, turn)],
            this.plan.MaximumMessagesPerRequest,
            this.plan.MaximumRequestCharacters,
            this.plan.MaximumRequestImageOctets);

        var answer = await this.AskAsync(turn, cancellationToken);

        if (answer is not { Text: { } answerText })
        {
            return this.Withhold(DayLayoutWithholding.ProviderUnavailable, answer?.Alias);
        }

        var suggestion = DayLayoutReading.Read(answerText, question);

        if (suggestion.Placements.Count is 0 && suggestion.NotToday.Count is 0)
        {
            DayLayoutEvents.LogAnswerUnreadable(this.logger, answer.Alias);
        }
        else
        {
            DayLayoutEvents.LogSuggested(
                this.logger,
                answer.Alias,
                suggestion.Placements.Count,
                question.Tasks.Count,
                suggestion.NotToday.Count);
        }

        return DayLayoutDerivation.Settled(suggestion);
    }

    /// <summary>Cuts a turn down to what one call may carry, without splitting a character in half.</summary>
    /// <remarks>
    /// A bound rather than a refusal, for the reason a derivation of mail states: a refusal here would be permanent
    /// for as long as the person's day stayed as it is, so a deployment whose endpoint is declared narrower than a
    /// long list would never arrange a day at all. What a bounded turn costs is the tail of the list, and an
    /// arrangement of the tasks the model saw is still an arrangement somebody can read.
    /// </remarks>
    private static string Bounded(string turn, int maximumCharacters) =>
        turn.Length <= maximumCharacters
            ? turn
            : MailTextBounds.TruncateAtTextElementBoundary(turn, maximumCharacters);

    /// <summary>Makes the one provider call, answering with nothing where it failed.</summary>
    /// <remarks>
    /// A failure is swallowed here rather than raised because the caller already has an outcome for that case, and the
    /// endpoint's own health record — which the resilience decorator wrote before this returned — is what a
    /// deployment's availability gate reads. A cancellation stays outside, being the person withdrawing the request
    /// rather than a provider failing to answer.
    /// </remarks>
    private async Task<ChatModelAnswer?> AskAsync(string turn, CancellationToken cancellationToken)
    {
        // One ledger for the whole chain, so an arrangement that falls through to the fallback spends the run's single
        // allowance across both attempts rather than opening a second one behind the first.
        var runLedger = new MailAnsweringRunLedger(this.runBounds);

        try
        {
            return await ChatModelFallThrough.RunAsync(
                this.plan,
                this.logger,
                (model, attemptToken) => this.AskModelAsync(model, turn, runLedger, attemptToken),
                cancellationToken);
        }
        catch (ChatGenerationFailedException failure)
        {
            // The alias the chain's last model failed under, which is what a line about this outage has to name. There
            // is no text, which is what tells the caller nothing answered.
            return new ChatModelAnswer(failure.EndpointAlias, Text: null);
        }
        catch (MailAnsweringBudgetExhaustedException)
        {
            // The per-request ceiling, which a single call reaches only where an operator declared a run smaller than
            // one call. It is a withholding rather than an empty answer for the same reason a spent period is: nothing
            // about the day decided it.
            return null;
        }
    }

    /// <summary>Asks one model of the chain, letting a failure out so the fallback behind it can be tried.</summary>
    private async Task<ChatModelAnswer> AskModelAsync(
        ChatGenerationPlan model,
        string turn,
        MailAnsweringRunLedger runLedger,
        CancellationToken cancellationToken)
    {
        // Against this model's own bounds rather than the main model's, because a fallback may be declared narrower
        // and a turn too wide for it is refused here rather than sent and billed for.
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

        // Outside the resilience decorator rather than inside it, so a call this deployment's own ceiling refused
        // never reaches the endpoint's circuit, its concurrency budget, or its health record. The run ledger is the
        // arrangement's own and is handed in, one day being one run however many models it was asked of.
        await using var chatClient = new BudgetedChatClient(
            resilientClient,
            runLedger,
            this.spendLedger);

        var agent = DayLayoutAgentComposition.Compose(
            chatClient,
            model,
            this.instructionEnvelope,
            this.loggerFactory);

        var response = await agent.RunAsync(turn, session: null, options: null, cancellationToken);

        return new ChatModelAnswer(endpoint.Alias, response.Text);
    }

    /// <summary>Withholds, naming the model this attempt reached where one was reached at all.</summary>
    private DayLayoutDerivation Withhold(DayLayoutWithholding withholding, string? answeringAlias = null)
    {
        DayLayoutEvents.LogWithheld(this.logger, answeringAlias ?? this.plan.Endpoint.Alias, withholding);

        return DayLayoutDerivation.Withholding(withholding);
    }
}

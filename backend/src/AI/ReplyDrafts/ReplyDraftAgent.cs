// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Emails.ReplyDrafts;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.ReplyDrafts;

/// <summary>Writes a reply by putting one conversation, one manner, and one ask to the composed drafting agent.</summary>
/// <remarks>
/// <para>
/// One call, no tools, and one conversation. What leaves this deployment is the subject, each message's author name,
/// when it was written and what it added, the names of the people in it, a few openings of the person's own recent
/// sent mail, and whatever they typed — every one of them guarded first. <b>No address leaves at all</b>: the people
/// are numbered, a proposal names a number, and the address it resolves to is looked up here afterwards.
/// </para>
/// <para>
/// It opens no chat call of its own. The agent is composed by <see cref="AgentComposition" /> like every other
/// operation in this product, so the instruction is carried where no turn can reach it, the tool set is the capability,
/// and the registered instruction envelope is wrapped around the instruction rather than written into it.
/// </para>
/// <para>
/// Every drafting is admitted against the same period ceilings a question is and counted by the same two ledgers, so
/// this is bounded rather than a second, unmetered way of spending a deployment's allowance — and it competes with
/// questions, with enrichment, and with a conversation's state for that allowance, which is the trade an operator makes
/// when they turn it on. A refused admission travels rather than falling back, because a person pressing a button the
/// operator has already spent the allowance on has to be told so.
/// </para>
/// <para>
/// A provider that fails, times out, or answers unreadably is not a failure a person is shown. What they have for that
/// case is the composer they were already looking at, which is the same composer a deployment that drafts nothing
/// serves.
/// </para>
/// <para>
/// Each drafting opens its own credential, transport, chat client, and agent, and releases all four with it. That is
/// the lifetime every other agent here uses and for the same reasons: a rotated key is picked up by the next drafting
/// rather than at the next restart, and one correspondence's text cannot outlive the call that sent it.
/// </para>
/// </remarks>
internal sealed class ReplyDraftAgent : IReplyDraftWriter
{
    private readonly ChatGenerationPlan plan;
    private readonly MailAnsweringRunLedger runLedger;
    private readonly IMailAnsweringSpendLedger spendLedger;
    private readonly IProviderEndpointCredentialSource credentialSource;
    private readonly OpenAiCompatibleClientFactory clientFactory;
    private readonly IHttpClientFactory transportFactory;
    private readonly IOutboundOperationRunner operationRunner;
    private readonly IAiProviderHealthRecorder healthRecorder;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly IAgentInstructionEnvelope instructionEnvelope;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<ReplyDraftAgent> logger;

    /// <summary>Creates the drafting one composer reaches the model through.</summary>
    /// <param name="plan">The generation parameters this deployment configured.</param>
    /// <param name="runLedger">Counts what this request has spent and refuses the call that would take it past its ceiling.</param>
    /// <param name="spendLedger">Counts what the current period has spent across every run.</param>
    /// <param name="credentialSource">Resolves the endpoint's credential for the one call.</param>
    /// <param name="clientFactory">Opens the provider client.</param>
    /// <param name="transportFactory">Supplies the named outbound transport that client speaks over.</param>
    /// <param name="operationRunner">Applies this deployment's outbound resilience to the call.</param>
    /// <param name="healthRecorder">Records what the endpoint did, so the availability gate can read it.</param>
    /// <param name="egressGuard">Withholds from the provider whatever this deployment withholds.</param>
    /// <param name="instructionEnvelope">The preamble and postamble every agent here carries.</param>
    /// <param name="loggerFactory">The factory the composed agent and the resilience decorator log through.</param>
    /// <param name="logger">Records the outcome without recording the reply or the correspondence.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public ReplyDraftAgent(
        ChatGenerationPlan plan,
        MailAnsweringRunLedger runLedger,
        IMailAnsweringSpendLedger spendLedger,
        IProviderEndpointCredentialSource credentialSource,
        OpenAiCompatibleClientFactory clientFactory,
        IHttpClientFactory transportFactory,
        IOutboundOperationRunner operationRunner,
        IAiProviderHealthRecorder healthRecorder,
        SensitiveContentEgressGuard egressGuard,
        IAgentInstructionEnvelope instructionEnvelope,
        ILoggerFactory loggerFactory,
        ILogger<ReplyDraftAgent> logger)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(runLedger);
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
        this.runLedger = runLedger;
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
    public async Task<ReplyDraft> WriteAsync(ReplyDraftBrief brief, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brief);

        var sources = brief.Sources;

        // Admitted against the period before anything is composed or scanned, so a deployment that has already spent
        // its allowance costs nothing to refuse — and so a drafting takes its place in the same count a question does
        // rather than being a second, unmetered way of reaching the provider. The refusal travels because a person
        // pressed a button: falling back here would leave them typing into a composer and waiting for a draft that
        // this deployment had already decided not to pay for.
        if (!await this.spendLedger.TryAdmitRunAsync(cancellationToken))
        {
            throw MailAnsweringBudgetExhaustedException.PeriodSpent();
        }

        var turn = Bounded(
            ReplyDraftInstructions.ComposeDraftingTurn(
                await this.GuardedTurnAsync(brief, cancellationToken)),
            this.plan.MaximumRequestCharacters);

        ChatRequestBounds.Require(
            [new ChatMessage(ChatRole.User, turn)],
            this.plan.MaximumMessagesPerRequest,
            this.plan.MaximumRequestCharacters,
            this.plan.MaximumRequestImageOctets);

        var answer = await this.AskAsync(turn, brief.Language, cancellationToken);
        var draft = ReplyDraftReading.Read(answer?.Text, sources.Messages, sources.Participants);

        // The model that answered where one did, and the model the brief was put to where none could.
        var answeringAlias = answer?.Alias ?? this.plan.Endpoint.Alias;

        if (draft.WasWritten)
        {
            var unsupportedClaimCount = draft.Claims.Count(static claim => !claim.IsSupported);

            ReplyDraftEvents.LogDrafted(
                this.logger,
                answeringAlias,
                sources.Messages.Count,
                draft.Claims.Count,
                unsupportedClaimCount,
                draft.ProposedRecipients.Count);
        }
        else
        {
            ReplyDraftEvents.LogAnswerUnreadable(this.logger, answeringAlias);
        }

        return draft;
    }

    /// <summary>Cuts a turn down to what one call may carry, without splitting a character in half.</summary>
    /// <remarks>
    /// A bound rather than a refusal, and the trade is the conversation's own derivation's: what a bounded turn loses
    /// is the tail of the style samples the turn ends with, which cost the reply its manner rather than its meaning.
    /// The correspondence itself was already bounded before it was read out of the store.
    /// </remarks>
    private static string Bounded(string turn, int maximumCharacters) =>
        turn.Length <= maximumCharacters
            ? turn
            : MailTextBounds.TruncateAtTextElementBoundary(turn, maximumCharacters);

    /// <summary>Puts every text of the drafting through the egress guard before any of it is composed into a turn.</summary>
    /// <remarks>
    /// The correspondence, the names, the person's own sent mail, and what they typed are all somebody's data and are
    /// scanned like every other text this deployment sends. The positions and the instants are this deployment's own
    /// and carry nothing anybody wrote, and an address is not offered because none is sent.
    /// </remarks>
    private async Task<GuardedDraftingTurn> GuardedTurnAsync(
        ReplyDraftBrief brief,
        CancellationToken cancellationToken)
    {
        var sources = brief.Sources;
        var messages = new List<GuardedDraftingMessage>(sources.Messages.Count);

        foreach (var message in sources.Messages)
        {
            messages.Add(new GuardedDraftingMessage(
                message.Position,
                await this.GuardedOptionalAsync(message.AuthorDisplayName, cancellationToken),
                message.SentAt,
                await this.GuardedAsync(message.Text, cancellationToken)));
        }

        var people = new List<GuardedDraftingPerson>(sources.Participants.Count);

        foreach (var participant in sources.Participants)
        {
            people.Add(new GuardedDraftingPerson(
                participant.Position,
                await this.GuardedOptionalAsync(participant.Address.DisplayName, cancellationToken)));
        }

        var styleSamples = new List<string>(sources.StyleSamples.Count);

        foreach (var sample in sources.StyleSamples)
        {
            styleSamples.Add(await this.GuardedAsync(sample, cancellationToken));
        }

        return new GuardedDraftingTurn(
            await this.GuardedOptionalAsync(sources.Subject, cancellationToken),
            people,
            messages,
            styleSamples,
            await this.GuardedOptionalAsync(brief.Selection, cancellationToken),
            await this.GuardedOptionalAsync(brief.Instruction, cancellationToken));
    }

    private Task<string> GuardedAsync(string text, CancellationToken cancellationToken) =>
        this.egressGuard.GuardAsync(SensitiveContentEgressPoint.ChatPrompt, text, cancellationToken);

    private Task<string?> GuardedOptionalAsync(string? text, CancellationToken cancellationToken) =>
        this.egressGuard.GuardOptionalAsync(SensitiveContentEgressPoint.ChatPrompt, text, cancellationToken);

    /// <summary>Makes the one provider call, answering with nothing where it failed.</summary>
    /// <remarks>
    /// <para>
    /// A failure is swallowed here rather than raised because the caller already has an outcome for that case — the
    /// composer as it was — and the endpoint's own health record, which the resilience decorator wrote before this
    /// returned, is what a deployment's availability gate reads.
    /// </para>
    /// <para>
    /// A spend ceiling is the exception and travels, so the boundary above can say which ceiling stopped a drafting
    /// rather than leaving somebody pressing a button that quietly does nothing. A cancellation stays outside, being
    /// the person withdrawing the request rather than a provider failing to answer it.
    /// </para>
    /// </remarks>
    private async Task<ChatModelAnswer?> AskAsync(
        string turn,
        MailUserLanguage language,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ChatModelFallThrough.RunAsync(
                this.plan,
                this.logger,
                (model, attemptToken) => this.AskModelAsync(model, turn, language, attemptToken),
                cancellationToken);
        }
        catch (ChatGenerationFailedException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            // The whole of what the credential source publishes: the alias names no endpoint the configuration in
            // force declares, or the secret behind it did not resolve. It is also the one failure here that leaves no
            // health record behind, the resilience decorator not yet existing to write one.
            return null;
        }
    }

    /// <summary>Asks one model of the chain, letting a failure out so the fallback behind it can be tried.</summary>
    private async Task<ChatModelAnswer> AskModelAsync(
        ChatGenerationPlan model,
        string turn,
        MailUserLanguage language,
        CancellationToken cancellationToken)
    {
        // Against this model's own bounds rather than the main model's, because a fallback may be declared
        // narrower and a conversation too wide for it is refused here rather than sent and billed for.
        ChatRequestBounds.RequireForAttempt([new ChatMessage(ChatRole.User, turn)], model);

        var endpoint = model.Endpoint;

        using var credential = await this.credentialSource.ResolveAsync(endpoint.Alias, cancellationToken);
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
        // never reaches the endpoint's circuit, its concurrency budget, or its health record. The two ledgers are
        // this request's and the period's: what an operator pays a provider does not change because the call was
        // started from a composer.
        await using var chatClient = new BudgetedChatClient(resilientClient, this.runLedger, this.spendLedger);

        var agent = ReplyDraftAgentComposition.Compose(
            chatClient,
            model,
            language,
            this.instructionEnvelope,
            this.loggerFactory);

        var response = await agent.RunAsync(turn, session: null, options: null, cancellationToken);

        return new ChatModelAnswer(endpoint.Alias, response.Text);
    }
}

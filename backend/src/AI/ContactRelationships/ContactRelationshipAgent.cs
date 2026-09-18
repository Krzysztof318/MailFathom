// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Contacts.Correspondence;
using MailFathom.Application.Contacts.Relationship;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.ContactRelationships;

/// <summary>Derives where a relationship stands by putting one person's correspondence to the composed agent.</summary>
/// <remarks>
/// <para>
/// One call, no tools, and one person. What leaves this deployment is each conversation's subject, each document's
/// name and declared type, and the instants beside them — every text of it guarded first. <b>No address, no contact
/// name, no folder, no account, and no message body travels with it</b>, because none of them is in the turn at all.
/// </para>
/// <para>
/// It opens no chat call of its own. The agent is composed by <see cref="AgentComposition" /> like every other
/// operation in this product, so the instruction is carried where no turn can reach it, the tool set is the capability,
/// and the registered instruction envelope is wrapped around the instruction rather than written into it.
/// </para>
/// <para>
/// Every derivation is admitted against the same period ceilings a question is and counted by the same two ledgers, so
/// this is bounded rather than a second, unmetered way of spending a deployment's allowance — and it competes with
/// questions, with drafting, and with a conversation's state for that allowance, which is the trade an operator makes
/// when they turn it on.
/// </para>
/// <para>
/// A refused admission withholds the card rather than failing the read, which is where this parts company with a
/// drafted reply: a draft is what somebody pressed a button for and a card is what a page brought with it, so refusing
/// the page over an operator's ceiling would take a contact away rather than a card. A provider that fails, times out,
/// or answers unreadably is withheld the same way and for the same reason.
/// </para>
/// <para>
/// Each derivation opens its own credential, transport, chat client, and agent, and releases all four with it. That is
/// the lifetime every other agent here uses and for the same reasons: a rotated key is picked up by the next contact
/// somebody opens rather than at the next restart, and one correspondence's text cannot outlive the call that sent it.
/// </para>
/// </remarks>
internal sealed class ContactRelationshipAgent : IContactRelationshipDeriver
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
    private readonly ILogger<ContactRelationshipAgent> logger;

    /// <summary>Creates the derivation an opened contact reaches the model through.</summary>
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
    /// <param name="logger">Records the outcome without recording the card or the correspondence.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public ContactRelationshipAgent(
        [FromKeyedServices(ChatCapability.ContactRelationship)] ChatGenerationPlan plan,
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
        ILogger<ContactRelationshipAgent> logger)
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
    public async Task<ContactRelationship> DeriveAsync(
        ContactRelationshipBrief brief,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brief);

        var correspondence = brief.Correspondence;

        // Admitted against the period before anything is composed or scanned, so a deployment that has already spent
        // its allowance costs nothing to refuse — and so a card takes its place in the same count a question does
        // rather than being a second, unmetered way of reaching the provider.
        if (!await this.spendLedger.TryAdmitRunAsync(cancellationToken))
        {
            ContactRelationshipEvents.LogAllowanceExhausted(this.logger, this.plan.Endpoint.Alias);

            return ContactRelationship.Nothing;
        }

        var turn = Bounded(
            ContactRelationshipInstructions.ComposeRelationshipTurn(
                await this.GuardedTurnAsync(correspondence, cancellationToken)),
            this.plan.MaximumRequestCharacters);

        ChatRequestBounds.Require(
            [new ChatMessage(ChatRole.User, turn)],
            this.plan.MaximumMessagesPerRequest,
            this.plan.MaximumRequestCharacters,
            this.plan.MaximumRequestImageOctets);

        var answer = await this.AskAsync(turn, brief.Language, cancellationToken);
        var card = ContactRelationshipReading.Read(answer?.Text, correspondence);

        // The model that answered where one did, and the model the brief was put to where none could.
        var answeringAlias = answer?.Alias ?? this.plan.Endpoint.Alias;

        if (card.WasDerived)
        {
            ContactRelationshipEvents.LogDerived(
                this.logger,
                answeringAlias,
                correspondence.Threads.Count,
                correspondence.Documents.Count,
                card.Observations.Count);
        }
        else
        {
            ContactRelationshipEvents.LogAnswerUnreadable(this.logger, answeringAlias);
        }

        return card;
    }

    /// <summary>Cuts a turn down to what one call may carry, without splitting a character in half.</summary>
    /// <remarks>
    /// A bound rather than a refusal, and the trade every derivation beside it makes: what a bounded turn loses is the
    /// tail of the documents, which are the oldest of them, so the card is derived from the recent end of a
    /// correspondence that was already bounded before it was read out of the index.
    /// </remarks>
    private static string Bounded(string turn, int maximumCharacters) =>
        turn.Length <= maximumCharacters
            ? turn
            : MailTextBounds.TruncateAtTextElementBoundary(turn, maximumCharacters);

    /// <summary>Puts every text of the correlation through the egress guard before any of it is composed into a turn.</summary>
    /// <remarks>
    /// The subject and the file name are the two values a sender wrote and are scanned like every other text this
    /// deployment sends — a subject quoting a key a colleague pasted has put that key into the request. The declared
    /// type and the instants are this deployment's own reading of a message and carry nothing anybody composed.
    /// </remarks>
    private async Task<GuardedRelationshipTurn> GuardedTurnAsync(
        ContactCorrespondence correspondence,
        CancellationToken cancellationToken)
    {
        var conversations = new List<GuardedRelationshipConversation>(correspondence.Threads.Count);

        foreach (var thread in correspondence.Threads)
        {
            conversations.Add(new GuardedRelationshipConversation(
                await this.GuardedOptionalAsync(thread.Subject, cancellationToken),
                thread.LastCorrespondedAt));
        }

        var documents = new List<GuardedRelationshipDocument>(correspondence.Documents.Count);

        foreach (var document in correspondence.Documents)
        {
            documents.Add(new GuardedRelationshipDocument(
                await this.GuardedOptionalAsync(document.FileName, cancellationToken),
                document.DeclaredMediaType,
                document.ReceivedAt));
        }

        return new GuardedRelationshipTurn(conversations, documents);
    }

    private Task<string?> GuardedOptionalAsync(string? text, CancellationToken cancellationToken) =>
        this.egressGuard.GuardOptionalAsync(SensitiveContentEgressPoint.ChatPrompt, text, cancellationToken);

    /// <summary>Makes the one provider call, answering with nothing where it failed.</summary>
    /// <remarks>
    /// <para>
    /// A failure is swallowed here rather than raised because the caller already has an outcome for that case — the
    /// contact page without the card — and the endpoint's own health record, which the resilience decorator wrote
    /// before this returned, is what a deployment's availability gate reads.
    /// </para>
    /// <para>
    /// The per-derivation ceiling is swallowed with it, which a single call reaches only where an operator declared a
    /// run smaller than one call. A cancellation stays outside, being the reader closing the contact rather than a
    /// provider failing to answer.
    /// </para>
    /// </remarks>
    private async Task<ChatModelAnswer?> AskAsync(
        string turn,
        MailAccountLanguage language,
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
        catch (ChatGenerationFailedException failure)
        {
            // The alias the chain's last model failed under, which is what a line about this outage has to name: the
            // model asked first has a fallback behind it, so naming that one sends a reader to an endpoint that may be
            // working. There is no text, which is what tells the caller nothing answered.
            return new ChatModelAnswer(failure.EndpointAlias, Text: null);
        }
        catch (MailAnsweringBudgetExhaustedException)
        {
            return null;
        }
    }

    /// <summary>Asks one model of the chain, letting a failure out so the fallback behind it can be tried.</summary>
    private async Task<ChatModelAnswer> AskModelAsync(
        ChatGenerationPlan model,
        string turn,
        MailAccountLanguage language,
        CancellationToken cancellationToken)
    {
        // Against this model's own bounds rather than the main model's, because a fallback may be declared narrower
        // and a correspondence too wide for it is refused here rather than sent and billed for.
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
        // never reaches the endpoint's circuit, its concurrency budget, or its health record.
        await using var chatClient = new BudgetedChatClient(
            resilientClient,
            this.runLedger,
            this.spendLedger);

        var agent = ContactRelationshipAgentComposition.Compose(
            chatClient,
            model,
            language,
            this.instructionEnvelope,
            this.loggerFactory);

        var response = await agent.RunAsync(turn, session: null, options: null, cancellationToken);

        return new ChatModelAnswer(endpoint.Alias, response.Text);
    }
}

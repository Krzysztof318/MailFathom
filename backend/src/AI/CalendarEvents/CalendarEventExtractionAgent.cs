// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Calendar.Extraction;
using MailFathom.Application.Chat;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Emails;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.CalendarEvents;

/// <summary>Reads text into calendar events by putting it to the composed extraction agent.</summary>
/// <remarks>
/// <para>
/// One call, no tools, and one text. What leaves this deployment is the subject and the passages of one message, or
/// one sentence somebody typed, every one of them guarded first — so a reading sends the text and nothing about the
/// mailbox, the person, or the calendar it may reach.
/// </para>
/// <para>
/// It opens no chat call of its own. The agent is composed by <see cref="AgentComposition" /> like every other
/// operation in this product, so the instruction is carried where no turn can reach it, the tool set is the
/// capability, and the registered instruction envelope is wrapped around the instruction rather than written into it.
/// </para>
/// <para>
/// Both halves are admitted against the same period ceilings a question is and counted by the same two ledgers, which
/// is what makes proposing events from mail bounded rather than a second, unmetered way of spending a deployment's
/// allowance. A refused admission withholds the reading rather than failing it, and the caller decides what that
/// means: the pass leaves the message outstanding, and the screen offers a form somebody fills in themselves.
/// </para>
/// <para>
/// A provider that failed, timed out, or was unreachable withholds for the same reason. A provider that
/// <em>answered</em> with something unreadable does not: the call was made and paid for, and repeating it buys the
/// same answer, so the text is settled as naming no event rather than being offered to the endpoint forever.
/// </para>
/// <para>
/// Each reading opens its own credential, transport, chat client, and agent, and releases all four with it. That is
/// the same lifetime every other agent here uses and for the same reasons: a rotated key is picked up by the next
/// reading rather than at the next restart, and one text cannot outlive the call that sent it.
/// </para>
/// </remarks>
internal sealed class CalendarEventExtractionAgent : ICalendarEventExtractor
{
    /// <summary>How many events a typed sentence may be read into.</summary>
    /// <remarks>
    /// Somebody describing a meeting is describing one, and the dialog it fills has one set of fields. A sentence read
    /// into three would present two the person never meant and has to notice in order to discard.
    /// </remarks>
    internal const int MaximumEventsPerDescription = 1;

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
    private readonly ILogger<CalendarEventExtractionAgent> logger;

    /// <summary>Initializes the reading over the declared endpoint, its credentials, and its transport.</summary>
    /// <param name="plan">The validated declaration: which endpoint reads and with which parameters.</param>
    /// <param name="runBounds">What one reading may send, call, and consume before it is stopped.</param>
    /// <param name="spendLedger">Admits one reading against the current period and counts what its call consumed.</param>
    /// <param name="credentialSource">Resolves the endpoint's credential for the one call.</param>
    /// <param name="clientFactory">Opens the provider client.</param>
    /// <param name="transportFactory">Supplies the named outbound transport that client speaks over.</param>
    /// <param name="operationRunner">Applies this deployment's outbound resilience to the call.</param>
    /// <param name="healthRecorder">Records what the endpoint did, so the availability gate can read it.</param>
    /// <param name="egressGuard">Withholds from the provider whatever this deployment withholds.</param>
    /// <param name="instructionEnvelope">The preamble and postamble every agent here carries.</param>
    /// <param name="loggerFactory">The factory the composed agent and the resilience decorator log through.</param>
    /// <param name="logger">Records the outcome without recording the text or what was read from it.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public CalendarEventExtractionAgent(
        [FromKeyedServices(ChatCapability.CalendarEventExtraction)] ChatGenerationPlan plan,
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
        ILogger<CalendarEventExtractionAgent> logger)
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
    /// <remarks>This one is registered only where a deployment turned the extraction on, so it is active by construction.</remarks>
    public bool IsActive => true;

    /// <inheritdoc />
    public async Task<CalendarEventExtraction> ProposeFromEmailAsync(
        EnrichableEmail email,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        if (email.Passages.Count is 0)
        {
            return CalendarEventExtraction.Settled([]);
        }

        // Nothing relative in a message can be resolved without knowing when it was sent, and almost every date in
        // mail is relative. A message the store could not date is therefore settled as naming nothing rather than
        // read against the current time, which would put last spring's meeting in next week.
        if (email.ReceivedAt is not { } receivedAt)
        {
            CalendarEventExtractionEvents.LogMessageNotAnchored(this.logger);

            return CalendarEventExtraction.Settled([]);
        }

        if (!await this.spendLedger.TryAdmitRunAsync(cancellationToken))
        {
            return this.Withhold(CalendarEventExtractionWithholding.AllowanceExhausted);
        }

        // The subject and the passages are somebody's mail, so they are scanned like every other text this deployment
        // sends: a message quoting a key a colleague pasted has put that key into the request.
        var subject = await this.egressGuard.GuardOptionalAsync(
            SensitiveContentEgressPoint.ChatPrompt,
            email.Subject,
            cancellationToken);
        var passages = await this.egressGuard.GuardAllAsync(
            SensitiveContentEgressPoint.ChatPrompt,
            [.. email.Passages.Select(static passage => passage.Text)],
            cancellationToken);

        var turn = CalendarEventExtractionInstructions.ComposeMailTurn(subject, receivedAt, passages);
        var answer = await this.AskAsync(turn, cancellationToken);

        if (answer is not { Text: { } answerText })
        {
            return this.Withhold(CalendarEventExtractionWithholding.ProviderUnavailable, answer?.Alias);
        }

        var events = CalendarEventExtractionReading.Read(
            answerText,
            receivedAt,
            CalendarEventExtraction.MaximumEvents);

        CalendarEventExtractionEvents.LogProposedFromMail(this.logger, answer.Alias, events.Count);

        return CalendarEventExtraction.Settled(events);
    }

    /// <inheritdoc />
    public async Task<CalendarEventExtraction> DraftFromDescriptionAsync(
        CalendarEventDescription description,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(description);

        if (!await this.spendLedger.TryAdmitRunAsync(cancellationToken))
        {
            return this.Withhold(CalendarEventExtractionWithholding.AllowanceExhausted);
        }

        // A sentence somebody typed is a prompt they wrote, and it is guarded exactly as mail is: what they are
        // arranging with whom is as revealing as the message that arranged it.
        var sentence = await this.egressGuard.GuardAsync(
            SensitiveContentEgressPoint.ChatPrompt,
            description.Text,
            cancellationToken);

        var turn = CalendarEventExtractionInstructions.ComposeDescriptionTurn(sentence, description.WrittenAt);
        var answer = await this.AskAsync(turn, cancellationToken);

        if (answer is not { Text: { } answerText })
        {
            return this.Withhold(CalendarEventExtractionWithholding.ProviderUnavailable, answer?.Alias);
        }

        var events = CalendarEventExtractionReading.Read(
            answerText,
            description.WrittenAt,
            MaximumEventsPerDescription);

        CalendarEventExtractionEvents.LogDraftedFromDescription(this.logger, answer.Alias, events.Count);

        return CalendarEventExtraction.Settled(events);
    }

    /// <summary>Cuts a turn down to what one call may carry, without splitting a character in half.</summary>
    /// <remarks>
    /// A bound rather than a refusal, on the same reading the enrichment beside it takes: a message composed longer
    /// than the declared endpoint accepts would be composed to that same length on every run, so refusing it would
    /// leave the message outstanding for ever and stop the pass each time it came round. What a bounded turn costs is
    /// the tail of the last passage, and an occasion read out of the opening of a message is still an occasion
    /// somebody is offered. A typed sentence is bounded at five hundred characters before it ever reaches here, so
    /// this touches it only where an operator declared an endpoint narrower than one sentence.
    /// </remarks>
    private static string Bounded(string turn, int maximumCharacters) =>
        turn.Length <= maximumCharacters
            ? turn
            : MailTextBounds.TruncateAtTextElementBoundary(turn, maximumCharacters);

    /// <summary>Makes the one provider call, answering with nothing where it failed.</summary>
    /// <remarks>
    /// A failure is swallowed here rather than raised because both callers already have an outcome for that case, and
    /// the endpoint's own health record — which the resilience decorator wrote before this returned — is what a
    /// deployment's availability gate reads. Opening the call is inside that guarantee rather than in front of it,
    /// because a reading that never reached the endpoint was withheld the same way as one the endpoint refused. A
    /// cancellation stays outside, being the caller withdrawing the work rather than a provider failing to answer.
    /// </remarks>
    private async Task<ChatModelAnswer?> AskAsync(string composed, CancellationToken cancellationToken)
    {
        var turn = Bounded(composed, this.plan.MaximumRequestCharacters);

        ChatRequestBounds.Require(
            [new ChatMessage(ChatRole.User, turn)],
            this.plan.MaximumMessagesPerRequest,
            this.plan.MaximumRequestCharacters,
            this.plan.MaximumRequestImageOctets);

        // One ledger for the whole chain, so a reading that falls through to the fallback spends the run's single
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
            // The alias the chain's last model failed under, which is what a line about this outage has to name: the
            // model asked first has a fallback behind it, so naming that one sends a reader to an endpoint that may be
            // working. There is no text, which is what tells the caller nothing answered.
            return new ChatModelAnswer(failure.EndpointAlias, Text: null);
        }
        catch (MailAnsweringBudgetExhaustedException)
        {
            // The per-reading ceiling, which a single call reaches only where an operator declared a run smaller than
            // one call. It is reported as the endpoint being unavailable rather than as a spent period, because
            // nothing about the period is exhausted and the next text would meet exactly the same refusal.
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
        // reading's own and is handed in, one text being one run however many models it was asked of.
        await using var chatClient = new BudgetedChatClient(
            resilientClient,
            runLedger,
            this.spendLedger);

        var agent = CalendarEventExtractionAgentComposition.Compose(
            chatClient,
            model,
            this.instructionEnvelope,
            this.loggerFactory);

        var response = await agent.RunAsync(turn, session: null, options: null, cancellationToken);

        return new ChatModelAnswer(endpoint.Alias, response.Text);
    }

    /// <summary>Withholds, naming the model this attempt reached where one was reached at all.</summary>
    /// <remarks>
    /// A chain that failed outright failed at its last model, and the alias the capability was configured with names
    /// the one asked first — an endpoint that may be working. Where nothing reached a model, that configured alias is
    /// the only one there is and is what the line carries.
    /// </remarks>
    private CalendarEventExtraction Withhold(
        CalendarEventExtractionWithholding withholding,
        string? answeringAlias = null)
    {
        CalendarEventExtractionEvents.LogWithheld(
            this.logger,
            answeringAlias ?? this.plan.Endpoint.Alias,
            withholding);

        return CalendarEventExtraction.Withholding(withholding);
    }
}

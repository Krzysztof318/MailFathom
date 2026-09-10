// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Emails.Search.Phrasing;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.Search;

/// <summary>Reads a typed sentence into the search it describes, by putting it to a composed agent and reading what came back.</summary>
/// <remarks>
/// <para>
/// One call, no tools, no mail. What leaves this deployment is the sentence somebody typed and the calendar day they
/// typed it on, so the reading costs a single short exchange and cannot be talked into retrieving anything.
/// </para>
/// <para>
/// A provider that fails, times out, or answers with something unreadable does not end the search: the reading falls
/// back to nothing, which leaves the typed words to be searched for exactly as a deployment with no model searches
/// them. That is deliberate — writing filters out of a sentence is a convenience over the search rather than the search
/// itself, and a person is not left without one because the derivation was unavailable for a moment.
/// </para>
/// <para>
/// <strong>A spend ceiling is the exception to that.</strong> A call the deployment's own ledgers refuse never leaves,
/// and the refusal propagates rather than falling back, so the boundary above can say which ceiling stopped it instead
/// of quietly searching something else.
/// </para>
/// </remarks>
internal sealed class MailSearchPhraseAgent : IMailSearchPhraseReader
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
    private readonly ILogger<MailSearchPhraseAgent> logger;

    /// <summary>Creates the reading one search reaches the model through.</summary>
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
    /// <param name="logger">The log this reading reports to.</param>
    public MailSearchPhraseAgent(
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
        ILogger<MailSearchPhraseAgent> logger)
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
    public async Task<MailSearchPhraseReading> ReadAsync(
        MailSearchPhrase phrase,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(phrase);

        var endpoint = this.plan.Endpoint;

        // The sentence is a prompt somebody wrote, so it is guarded like every other text that leaves this deployment:
        // somebody looking for the message a colleague sent them a key in has put that key into the request.
        var phraseText = await this.egressGuard.GuardAsync(
            SensitiveContentEgressPoint.ChatPrompt,
            phrase.Text.Value,
            cancellationToken);

        var turn = MailSearchPhraseInstructions.ComposeReadingTurn(phraseText, phrase.AskedOn);

        ChatRequestBounds.Require(
            [new ChatMessage(ChatRole.User, turn)],
            this.plan.MaximumMessagesPerRequest,
            this.plan.MaximumRequestCharacters,
            this.plan.MaximumRequestImageOctets);

        var answerText = await this.AskAsync(turn, cancellationToken);
        var reading = MailSearchPhraseDocumentReading.Read(answerText);

        if (reading.WasRead)
        {
            var filterCount = CountFilters(reading.Filters);

            MailSearchPhraseEvents.LogPhraseRead(
                this.logger,
                endpoint.Alias,
                filterCount,
                reading.Criteria.Count);
        }
        else
        {
            MailSearchPhraseEvents.LogPhraseUnreadable(this.logger, endpoint.Alias);
        }

        return reading;
    }

    /// <summary>How many constraints a reading produced, which is all a log may say about one.</summary>
    private static int CountFilters(MailSearchPhraseFilters filters) =>
        (filters.SenderAddress is null ? 0 : 1)
        + (filters.RecipientAddress is null ? 0 : 1)
        + (filters.ReceivedFrom is null ? 0 : 1)
        + (filters.ReceivedTo is null ? 0 : 1)
        + (filters.Unread ? 1 : 0)
        + (filters.Flagged ? 1 : 0)
        + (filters.HasAttachments ? 1 : 0);

    /// <summary>Makes the one provider call, answering with nothing where it failed.</summary>
    /// <remarks>
    /// <para>
    /// A failure is swallowed here rather than raised because the caller already has a usable search for that case —
    /// the words somebody typed — and the endpoint's own health record, which the resilience decorator wrote before this
    /// returned, is what an availability gate reads.
    /// </para>
    /// <para>
    /// Opening the call is inside that guarantee rather than in front of it, because a reading that never reached the
    /// endpoint failed the same way as one the endpoint refused. A cancellation stays outside, being the person
    /// withdrawing the search rather than a provider failing to read it.
    /// </para>
    /// </remarks>
    private async Task<string?> AskAsync(string turn, CancellationToken cancellationToken)
    {
        var endpoint = this.plan.Endpoint;

        try
        {
            // Opened per reading and released with it, so a rotated key is picked up by the next search and the
            // material exists for one call rather than for process uptime.
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

            // Outside the resilience decorator rather than inside it, so a call this deployment's own ceiling refused
            // never reaches the endpoint's circuit, its concurrency budget, or its health record. The two ledgers are
            // this request's and the period's, and a search is charged to the same allowance a question is: what an
            // operator pays a provider does not change because the call was started from a search field.
            await using var chatClient = new BudgetedChatClient(resilientClient, this.runLedger, this.spendLedger);

            var agent = MailSearchPhraseAgentComposition.Compose(
                chatClient,
                this.plan,
                this.instructionEnvelope,
                this.loggerFactory);

            var response = await agent.RunAsync(turn, session: null, options: null, cancellationToken);

            return response.Text;
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
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.Discovery;

/// <summary>Composes what a run found into a result, by putting the question and its extracts to a composed agent and reading what came back.</summary>
/// <remarks>
/// <para>
/// One call, no tools, and no way back to the mailbox. The sources are minted before the model sees anything and it may
/// only cite the names it was given, so nothing it writes becomes a reference to mail — which is what lets a claim
/// resting on an invented name be read as a claim resting on nothing.
/// </para>
/// <para>
/// A provider that fails, times out, or answers with something unreadable does not end the run: the reading composes a
/// result saying the sources do not answer the question, over the same citations and the same account coverage. That is
/// the honest answer for a run whose model was unavailable, and it is the answer a person is owed rather than an error
/// page — the mail was read, and what could not be done was the reading of it.
/// </para>
/// <para>
/// <strong>A spend ceiling is the exception to that, and it is meant to be.</strong> A call the run's own ledger refuses
/// never leaves and there is nothing to compose a result from, so the refusal propagates and the run ends stating which
/// ceiling stopped it. A fallback there would tell somebody their mail holds no answer when what happened is that the
/// deployment declined to pay for one.
/// </para>
/// </remarks>
internal sealed class DiscoveryCompositionAgent : IDiscoveryResultComposer
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
    private readonly ILogger<DiscoveryCompositionAgent> logger;

    /// <summary>Creates the composition one Discover run reaches the model through.</summary>
    /// <param name="plan">The generation parameters this deployment configured.</param>
    /// <param name="runLedger">Counts what this run has spent and refuses the call that would take it past its ceiling.</param>
    /// <param name="spendLedger">Counts what the current period has spent across every run.</param>
    /// <param name="credentialSource">Resolves the endpoint's credential for the one call.</param>
    /// <param name="clientFactory">Opens the provider client.</param>
    /// <param name="transportFactory">Supplies the named outbound transport that client speaks over.</param>
    /// <param name="operationRunner">Applies this deployment's outbound resilience to the call.</param>
    /// <param name="healthRecorder">Records what the endpoint did, so the availability gate can read it.</param>
    /// <param name="egressGuard">Withholds from the provider whatever this deployment withholds.</param>
    /// <param name="instructionEnvelope">The preamble and postamble every agent here carries.</param>
    /// <param name="loggerFactory">The factory the composed agent and the resilience decorator log through.</param>
    /// <param name="logger">The log this composition reports to.</param>
    public DiscoveryCompositionAgent(
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
        ILogger<DiscoveryCompositionAgent> logger)
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
    public async Task<PresentationPlan> ComposeAsync(
        MailQuestion question,
        DiscoveryRunPlan plan,
        DiscoveryEvidence evidence,
        IReadOnlyList<AccountCoverage> coverage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(coverage);

        var sources = DiscoveryComposedSources.Declare(evidence.Passages);

        var turn = DiscoveryCompositionInstructions.ComposeCompositionTurn(
            await this.egressGuard.GuardAsync(
                SensitiveContentEgressPoint.ChatPrompt,
                question.Text.Value,
                cancellationToken),
            plan.Intent,
            await this.GuardAsync(sources, cancellationToken));

        var answer = await this.AskAsync(turn, cancellationToken);
        var composed = DiscoveryCompositionReading.Read(answer?.Text, plan, sources, evidence, coverage);

        if (answer is { Text: not null })
        {
            DiscoveryCompositionEvents.LogResultComposed(
                this.logger,
                answer.Alias,
                composed.Blocks.Count,
                composed.Citations.Count,
                composed.Blocks[0].Evidence.Support);
        }

        return composed;
    }

    /// <summary>Withholds from the provider whatever this deployment withholds, source by source.</summary>
    /// <remarks>
    /// The extracts leave this deployment and the plan does not, so only this copy is guarded: what the plan quotes is
    /// the user's own mail going back to the user, and redacting it there would hide from somebody what they already
    /// have. A guard that is inactive returns the same text, so a deployment scanning nothing pays nothing here.
    /// </remarks>
    private async Task<IReadOnlyList<DiscoveryTurnSource>> GuardAsync(
        IReadOnlyList<DiscoveryComposedSource> sources,
        CancellationToken cancellationToken)
    {
        if (!this.egressGuard.IsActive)
        {
            return
            [
                .. sources.Select(source => new DiscoveryTurnSource(
                    source.Citation.Id.Value,
                    source.Citation.Label.Value,
                    source.Extract)),
            ];
        }

        var guarded = new List<DiscoveryTurnSource>(sources.Count);

        foreach (var source in sources)
        {
            guarded.Add(new DiscoveryTurnSource(
                source.Citation.Id.Value,
                await this.egressGuard.GuardAsync(
                    SensitiveContentEgressPoint.ChatPrompt,
                    source.Citation.Label.Value,
                    cancellationToken),
                await this.egressGuard.GuardAsync(
                    SensitiveContentEgressPoint.ChatPrompt,
                    source.Extract,
                    cancellationToken)));
        }

        return guarded;
    }

    /// <summary>Makes the one provider call, answering with nothing where it failed.</summary>
    /// <remarks>
    /// A failure is swallowed here for the reason the derivation's is: the caller already composes a usable result for
    /// that case, and the endpoint's own health record — which the resilience decorator wrote before this returned — is
    /// what a run's availability gate reads. What differs is what is lost: a failed derivation costs a worse plan,
    /// while a failed composition costs the answer itself, and the result says exactly that rather than pretending to
    /// one.
    /// <para>
    /// The request bound is applied inside that same handling rather than in front of it, because it refuses rather
    /// than truncates. This turn carries the run's extracts, so a deployment whose passage ceiling and request ceiling
    /// leave it no room reaches the bound on an ordinary question — and a run that ended in an unhandled refusal would
    /// be a composition that failed, which is the one thing this one does not do.
    /// </para>
    /// </remarks>
    private async Task<ChatModelAnswer?> AskAsync(string turn, CancellationToken cancellationToken)
    {
        var endpoint = this.plan.Endpoint;

        try
        {
            ChatRequestBounds.Require(
                [new ChatMessage(ChatRole.User, turn)],
                this.plan.MaximumMessagesPerRequest,
                this.plan.MaximumRequestCharacters,
                this.plan.MaximumRequestImageOctets);
        }
        catch (ArgumentException)
        {
            // The turn is past what this deployment sends in one request, which is a question whose mail does not fit
            // rather than a defect: the result says the sources do not answer it, and the operator's own ceilings are
            // what decides whether a mailbox this size is answerable here. Caught around the bound alone, because
            // every argument failure below it is a wiring defect that must not read as a mailbox holding no answer.
            DiscoveryCompositionEvents.LogRequestPastItsBound(this.logger, endpoint.Alias);

            return null;
        }

        try
        {
            return await ChatModelFallThrough.RunAsync(
                this.plan,
                this.logger,
                (model, attemptToken) => this.AskModelAsync(model, turn, attemptToken),
                cancellationToken);
        }
        catch (ChatGenerationFailedException failure)
        {
            // The alias the chain's last model failed under rather than the one this composition was asked of, so a
            // line about the outage names the endpoint that actually refused it.
            DiscoveryCompositionEvents.LogGenerationFailed(this.logger, failure.EndpointAlias);

            return new ChatModelAnswer(failure.EndpointAlias, Text: null);
        }
        catch (InvalidOperationException)
        {
            // The whole of what the credential source publishes: the alias names no endpoint the configuration in
            // force declares, or the secret behind it did not resolve. It is also the one failure here that leaves no
            // health record behind, the resilience decorator not yet existing to write one.
            DiscoveryCompositionEvents.LogEndpointUnresolved(this.logger, endpoint.Alias);

            return null;
        }
    }

    /// <summary>Asks one model of the chain, letting a failure out so the fallback behind it can be tried.</summary>
    private async Task<ChatModelAnswer> AskModelAsync(
        ChatGenerationPlan model,
        string turn,
        CancellationToken cancellationToken)
    {
        // Against this model's own bounds rather than the main model's, because a fallback may be declared
        // narrower and a conversation too wide for it is refused here rather than sent and billed for.
        ChatRequestBounds.RequireForAttempt([new ChatMessage(ChatRole.User, turn)], model);

        var endpoint = model.Endpoint;

        // Opened per composition and released with it, so a rotated key is picked up by the next question and the
        // material exists for one call rather than for process uptime.
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
        // the run's and the period's: this call is one of the two a Discover run makes, and both are charged.
        await using var chatClient = new BudgetedChatClient(resilientClient, this.runLedger, this.spendLedger);

        var agent = DiscoveryCompositionAgentComposition.Compose(
            chatClient,
            model,
            this.instructionEnvelope,
            this.loggerFactory);

        var response = await agent.RunAsync(turn, session: null, options: null, cancellationToken);

        if (string.IsNullOrWhiteSpace(response.Text))
        {
            DiscoveryCompositionEvents.LogResultUnreadable(this.logger, endpoint.Alias);

            return new ChatModelAnswer(endpoint.Alias, Text: null);
        }

        return new ChatModelAnswer(endpoint.Alias, response.Text);
    }
}

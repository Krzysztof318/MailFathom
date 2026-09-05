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
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval;
using MailFathom.Application.SensitiveContent.Egress;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.Discovery;

/// <summary>Derives what one question decided, by putting it to a composed agent and reading what came back.</summary>
/// <remarks>
/// <para>
/// One call, no tools, no mail. What leaves this deployment is the person's own question and a count describing the
/// scope, so the derivation costs a single short exchange and cannot be talked into retrieving anything by what it is
/// planning over.
/// </para>
/// <para>
/// A provider that fails, times out, or answers with something unreadable does not end the run: the reading falls back
/// to the question's own words, which is the plan a deployment with no model would run. That is deliberate — the plan
/// is an optimization of retrieval rather than the answer, and a question is not unanswerable because the derivation
/// was unavailable for a moment.
/// </para>
/// </remarks>
internal sealed class DiscoveryPlanningAgent : IDiscoveryRunPlanner
{
    private readonly ChatGenerationPlan plan;
    private readonly EmailKnowledgeBounds retrievalBounds;
    private readonly IProviderEndpointCredentialSource credentialSource;
    private readonly OpenAiCompatibleClientFactory clientFactory;
    private readonly IHttpClientFactory transportFactory;
    private readonly IOutboundOperationRunner operationRunner;
    private readonly IAiProviderHealthRecorder healthRecorder;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly IAgentInstructionEnvelope instructionEnvelope;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<DiscoveryPlanningAgent> logger;

    /// <summary>Creates the derivation one Discover run reaches the model through.</summary>
    /// <param name="plan">The generation parameters this deployment configured.</param>
    /// <param name="retrievalBounds">What this deployment's retrieval returns at most, which bounds what a plan may ask for.</param>
    /// <param name="credentialSource">Resolves the endpoint's credential for the one call.</param>
    /// <param name="clientFactory">Opens the provider client.</param>
    /// <param name="transportFactory">Supplies the named outbound transport that client speaks over.</param>
    /// <param name="operationRunner">Applies this deployment's outbound resilience to the call.</param>
    /// <param name="healthRecorder">Records what the endpoint did, so the availability gate can read it.</param>
    /// <param name="egressGuard">Withholds from the provider whatever this deployment withholds.</param>
    /// <param name="instructionEnvelope">The preamble and postamble every agent here carries.</param>
    /// <param name="loggerFactory">The factory the composed agent and the resilience decorator log through.</param>
    /// <param name="logger">The log this derivation reports to.</param>
    public DiscoveryPlanningAgent(
        ChatGenerationPlan plan,
        EmailKnowledgeBounds retrievalBounds,
        IProviderEndpointCredentialSource credentialSource,
        OpenAiCompatibleClientFactory clientFactory,
        IHttpClientFactory transportFactory,
        IOutboundOperationRunner operationRunner,
        IAiProviderHealthRecorder healthRecorder,
        SensitiveContentEgressGuard egressGuard,
        IAgentInstructionEnvelope instructionEnvelope,
        ILoggerFactory loggerFactory,
        ILogger<DiscoveryPlanningAgent> logger)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(retrievalBounds);
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
        this.retrievalBounds = retrievalBounds;
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
    public async Task<DiscoveryRunPlan> DerivePlanAsync(MailQuestion question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);

        var endpoint = this.plan.Endpoint;

        // The question is a prompt somebody wrote, so it is guarded like every other text that leaves this deployment:
        // somebody asking what to do about the key a colleague sent them has put that key into the request.
        var questionText = await this.egressGuard.GuardAsync(
            SensitiveContentEgressPoint.ChatPrompt,
            question.Text.Value,
            cancellationToken);

        var turn = DiscoveryPlanningInstructions.ComposePlanningTurn(
            questionText,
            question.Scope,
            this.retrievalBounds);

        ChatRequestBounds.Require(
            [new ChatMessage(ChatRole.User, turn)],
            this.plan.MaximumMessagesPerRequest,
            this.plan.MaximumRequestCharacters);

        var answerText = await this.AskAsync(turn, cancellationToken);
        var outcome = DiscoveryPlanReading.Read(answerText, question.Text, this.retrievalBounds);

        if (outcome.WasRead)
        {
            DiscoveryPlanningEvents.LogPlanDerived(
                this.logger,
                endpoint.Alias,
                outcome.Plan.Intent.Identity,
                outcome.Plan.Retrieval.Lookups.Count,
                outcome.Plan.Retrieval.SufficientPassages);
        }
        else
        {
            DiscoveryPlanningEvents.LogPlanUnreadable(this.logger, endpoint.Alias);
        }

        return outcome.Plan;
    }

    /// <summary>Makes the one provider call, answering with nothing where it failed.</summary>
    /// <remarks>
    /// A failure is swallowed here rather than raised because the caller already has a usable plan for that case, and
    /// the endpoint's own health record — which the resilience decorator wrote before this returned — is what a run's
    /// availability gate reads. Losing the derivation is a worse plan; losing the run would be no answer at all.
    /// </remarks>
    private async Task<string?> AskAsync(string turn, CancellationToken cancellationToken)
    {
        var endpoint = this.plan.Endpoint;

        // Opened per derivation and released with it, so a rotated key is picked up by the next question and the
        // material exists for one call rather than for process uptime. It is the sequence an answering run opens with
        // as well; the run ledger is deliberately absent, for the reason below.
        using var credential = await this.credentialSource.ResolveAsync(endpoint.Alias, cancellationToken);
        using var transport = this.transportFactory.CreateClient(ProviderChatModelClient.TransportName);
        using var providerClient = this.clientFactory.OpenChatClient(endpoint, credential, transport);

        // ponytail: no spend ledger around this call — one turn with no tools cannot iterate, so the ceiling a run
        // ledger enforces has nothing to bound here. What it does mean is that the tokens a derivation costs are
        // unaccounted; wrap this in the run's own budget when #1172 gives a Discover run one.
        using var chatClient = new ResilientChatClient(
            providerClient,
            endpoint,
            this.plan.RequestTimeout,
            this.operationRunner,
            this.healthRecorder,
            this.loggerFactory.CreateLogger<ResilientChatClient>());

        var agent = DiscoveryPlanningAgentComposition.Compose(
            chatClient,
            this.plan,
            this.instructionEnvelope,
            this.loggerFactory);

        try
        {
            var response = await agent.RunAsync(turn, session: null, options: null, cancellationToken);

            return response.Text;
        }
        catch (ChatGenerationFailedException)
        {
            return null;
        }
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.EmailContent.Cleaning;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Emails;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.BodyCleanup;

/// <summary>Proposes which blocks of one body a reader is shown, by putting its outline to the composed agent.</summary>
/// <remarks>
/// <para>
/// One call, no tools, and one outline. What leaves this deployment is the subject, the envelope sender, and per block its
/// kind, its link count and the opening of its text — every one of them guarded first. No address beyond the sender, no
/// folder, no account, and no part of a block beyond its opening travels with it, and the answer carries no text at all.
/// </para>
/// <para>
/// It opens no chat call of its own. The agent is composed by <see cref="AgentComposition" /> like every other operation
/// in this product, so the instruction is carried where no turn can reach it, the tool set is the capability, and the
/// registered instruction envelope is wrapped around the instruction rather than written into it.
/// </para>
/// <para>
/// Every proposal is admitted against the same period ceilings a question is, and every call is counted by the same two
/// ledgers. That is what makes this bounded rather than a second, unmetered way of spending a deployment's allowance — and
/// it is also why it competes with questions, with enrichment and with a conversation's state for that allowance, which is
/// the trade an operator makes when they turn it on. A refused admission withholds the cleaning rather than failing the
/// read: the reader is shown the message, uncleaned, and told so.
/// </para>
/// <para>
/// The plan is this pass's own rather than the deployment's shared one, because <c>Chat:BodyCleanup:Model</c> may name a
/// model other than the one questions run on. Everything else in it is the endpoint's.
/// </para>
/// <para>
/// Each proposal opens its own credential, transport, chat client, and agent, and releases all four with it. That is the
/// same lifetime every other agent here uses and for the same reasons: a rotated key is picked up by the next open rather
/// than at the next restart, and one message's outline cannot outlive the call that sent it.
/// </para>
/// </remarks>
internal sealed class MailBodyCleanupAgent : IMailBodyCleaner
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
    private readonly ILogger<MailBodyCleanupAgent> logger;

    /// <summary>Initializes the pass over the declared endpoint, its credentials, and its transport.</summary>
    /// <param name="plan">This pass's own plan: the declared endpoint with whichever model the pass routes to.</param>
    /// <param name="runBounds">What one proposal may send, call, and consume before it is stopped.</param>
    /// <param name="spendLedger">Admits one proposal against the current period and counts what its call consumed.</param>
    /// <param name="credentialSource">Resolves the endpoint's credential for the one call.</param>
    /// <param name="clientFactory">Opens the provider client.</param>
    /// <param name="transportFactory">Supplies the named outbound transport that client speaks over.</param>
    /// <param name="operationRunner">Applies this deployment's outbound resilience to the call.</param>
    /// <param name="healthRecorder">Records what the endpoint did, so the availability gate can read it.</param>
    /// <param name="egressGuard">Withholds from the provider whatever this deployment withholds.</param>
    /// <param name="instructionEnvelope">The preamble and postamble every agent here carries.</param>
    /// <param name="loggerFactory">The factory the composed agent and the resilience decorator log through.</param>
    /// <param name="logger">Records the outcome without recording the message or which of its blocks were dropped.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailBodyCleanupAgent(
        MailBodyCleanupPlan plan,
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
        ILogger<MailBodyCleanupAgent> logger)
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

        this.plan = plan.Plan;
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
    /// <remarks>This one is registered only where a deployment turned the pass on, so it is active by construction.</remarks>
    public bool IsActive => true;

    /// <inheritdoc />
    public async Task<MailBodyCleaningProposal> ProposeAsync(
        CleanableMailBody body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        // Admitted before anything is composed or scanned, so a period this deployment has already spent costs nothing to
        // refuse. The count is what stops this from spending an allowance a question would otherwise have.
        if (!await this.spendLedger.TryAdmitRunAsync(cancellationToken))
        {
            return this.Withhold(MailBodyCleaningWithholding.AllowanceExhausted);
        }

        var turn = Bounded(
            MailBodyCleanupInstructions.ComposeOutlineTurn(await this.GuardedAsync(body, cancellationToken)),
            this.plan.MaximumRequestCharacters);

        ChatRequestBounds.Require(
            [new ChatMessage(ChatRole.User, turn)],
            this.plan.MaximumMessagesPerRequest,
            this.plan.MaximumRequestCharacters,
            this.plan.MaximumRequestImageOctets);

        if (await this.AskAsync(turn, cancellationToken) is not { } answerText)
        {
            return this.Withhold(MailBodyCleaningWithholding.ProviderUnavailable);
        }

        var segments = MailBodyCleanupReading.Read(answerText);

        if (segments.Count == 0)
        {
            MailBodyCleanupEvents.LogAnswerUnreadable(this.logger, this.plan.Endpoint.Alias, body.Blocks.Count);
        }
        else
        {
            MailBodyCleanupEvents.LogProposed(
                this.logger,
                this.plan.Endpoint.Alias,
                segments.Count,
                body.Blocks.Count);
        }

        return MailBodyCleaningProposal.Proposing(segments);
    }

    /// <summary>Cuts a turn down to what one call may carry, without splitting a character in half.</summary>
    /// <remarks>
    /// A bound rather than a refusal, and the trade here is the gentlest of the three passes that make it: what a cut costs
    /// is the tail of the outline, so the blocks past the cut are ones the answer will not name — which the partition check
    /// then reads as an answer that did not cover the document, and the reader is shown the uncleaned message. A truncated
    /// turn therefore cannot produce a cleaning that silently dropped the end of a message.
    /// </remarks>
    private static string Bounded(string turn, int maximumCharacters) =>
        turn.Length <= maximumCharacters
            ? turn
            : MailTextBounds.TruncateAtTextElementBoundary(turn, maximumCharacters);

    /// <summary>Puts every text of the outline through the egress guard before any of it is composed into a turn.</summary>
    /// <remarks>
    /// The subject, the sender and every block opening are somebody's mail and are scanned like every other text this
    /// deployment sends: a message quoting a key a colleague pasted has put that key into the opening of a block. The
    /// index, the kind and the link count are this deployment's own readings of the markup and carry nothing a sender
    /// wrote.
    /// </remarks>
    private async Task<CleanableMailBody> GuardedAsync(CleanableMailBody body, CancellationToken cancellationToken)
    {
        // One outline is one payload, so it is reported as one operation: what an operator waits on is the whole of what
        // leaves before a message can be drawn, which a percentile over each block opening cannot say.
        using var scan = this.egressGuard.BeginGuardedOperation(
            SensitiveContentEgressPoint.ChatPrompt,
            cancellationToken);

        var subject = await this.egressGuard.GuardOptionalAsync(
            SensitiveContentEgressPoint.ChatPrompt,
            body.Subject,
            cancellationToken);

        var senderName = await this.egressGuard.GuardOptionalAsync(
            SensitiveContentEgressPoint.ChatPrompt,
            body.SenderName,
            cancellationToken);

        var openings = await this.egressGuard.GuardAllAsync(
            SensitiveContentEgressPoint.ChatPrompt,
            [.. body.Blocks.Select(block => block.Opening)],
            cancellationToken);

        scan.Completed();

        return new CleanableMailBody(
            subject,
            senderName,
            [.. body.Blocks.Select((block, index) => block with { Opening = openings[index] })]);
    }

    /// <summary>Makes the provider call, against the fallback model too where the first could not answer, and answers with nothing where none could.</summary>
    /// <remarks>
    /// A failure is swallowed here rather than raised because the caller already has an outcome for that case, and the
    /// endpoint's own health record — which the resilience decorator wrote before this returned — is what a deployment's
    /// availability gate reads. A cancellation stays outside, being the reader withdrawing the wait rather than a provider
    /// failing to answer.
    /// </remarks>
    private async Task<string?> AskAsync(string turn, CancellationToken cancellationToken)
    {
        // One ledger for the whole chain, so a proposal that falls through to the fallback spends the run's
        // single allowance across both attempts rather than opening a second one behind the first.
        var runLedger = new MailAnsweringRunLedger(this.runBounds);

        try
        {
            return await ChatModelFallThrough.RunAsync(
                this.plan,
                this.logger,
                (model, attemptToken) => this.AskModelAsync(model, turn, runLedger, attemptToken),
                cancellationToken);
        }
        catch (ChatGenerationFailedException)
        {
            return null;
        }
        catch (MailAnsweringBudgetExhaustedException)
        {
            // The per-proposal ceiling, which a single call reaches only where an operator declared a run smaller than one
            // call. It is a withholding rather than an unreadable answer for the same reason a spent period is: nothing
            // about the message decided it.
            return null;
        }
        catch (InvalidOperationException)
        {
            // The whole of what the credential source publishes: the alias names no endpoint the configuration in force
            // declares, or the secret behind it did not resolve. It is also the one failure here that leaves no health
            // record behind, the resilience decorator not yet existing to write one.
            return null;
        }
    }

    /// <summary>Asks one model of the chain, letting a failure out so the fallback behind it can be tried.</summary>
    private async Task<string?> AskModelAsync(
        ChatGenerationPlan model,
        string turn,
        MailAnsweringRunLedger runLedger,
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
        // never reaches the endpoint's circuit, its concurrency budget, or its health record. The run ledger is this
        // the proposal's own and is handed in, one open being one run however many models it was asked of.
        await using var chatClient = new BudgetedChatClient(
            resilientClient,
            runLedger,
            this.spendLedger);

        var agent = MailBodyCleanupAgentComposition.Compose(
            chatClient,
            model,
            this.instructionEnvelope,
            this.loggerFactory);

        var response = await agent.RunAsync(turn, session: null, options: null, cancellationToken);

        return response.Text;
    }

    private MailBodyCleaningProposal Withhold(MailBodyCleaningWithholding withholding)
    {
        MailBodyCleanupEvents.LogWithheld(this.logger, this.plan.Endpoint.Alias, withholding);

        return MailBodyCleaningProposal.Withheld(withholding);
    }
}

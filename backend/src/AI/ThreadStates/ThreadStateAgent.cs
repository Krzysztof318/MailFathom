// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.ThreadStates;

/// <summary>Derives where one conversation stands by putting it to the composed thread-state agent.</summary>
/// <remarks>
/// <para>
/// One call, no tools, and one conversation. What leaves this deployment is the subject, and for each message the name
/// it was written under, when it was written, and what it added — every one of them guarded first. No address, no
/// folder, and no account travels with it.
/// </para>
/// <para>
/// It opens no chat call of its own. The agent is composed by <see cref="AgentComposition" /> like every other
/// operation in this product, so the instruction is carried where no turn can reach it, the tool set is the capability,
/// and the registered instruction envelope is wrapped around the instruction rather than written into it.
/// </para>
/// <para>
/// Every derivation is admitted against the same period ceilings a question is, and every call is counted by the same
/// two ledgers. That is what makes this bounded rather than a second, unmetered way of spending a deployment's
/// allowance — and it is also why it competes with questions and with enrichment for that allowance, which is the trade
/// an operator makes when they turn it on. A refused admission withholds the derivation rather than failing it: the
/// conversation stays outstanding and the next period reaches it.
/// </para>
/// <para>
/// A conversation past what one derivation takes in never reaches the provider at all. The store hands it over with no
/// messages, and the answer here is a settled record saying the conversation is too long — never a state derived from
/// the part of it that would have fitted, which would read on a screen exactly like one derived from the whole.
/// </para>
/// <para>
/// Each derivation opens its own credential, transport, chat client, and agent, and releases all four with it. That is
/// the same lifetime every other agent here uses and for the same reasons: a rotated key is picked up by the next
/// conversation rather than at the next restart, and one exchange's text cannot outlive the call that sent it.
/// </para>
/// </remarks>
internal sealed class ThreadStateAgent : IThreadStateDeriver
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
    private readonly ILogger<ThreadStateAgent> logger;

    /// <summary>Initializes the derivation over the declared endpoint, its credentials, and its transport.</summary>
    /// <param name="plan">The validated declaration: which endpoint derives and with which parameters.</param>
    /// <param name="runBounds">What one derivation may send, call, and consume before it is stopped.</param>
    /// <param name="spendLedger">Admits one derivation against the current period and counts what its call consumed.</param>
    /// <param name="credentialSource">Resolves the endpoint's credential for the one call.</param>
    /// <param name="clientFactory">Opens the provider client.</param>
    /// <param name="transportFactory">Supplies the named outbound transport that client speaks over.</param>
    /// <param name="operationRunner">Applies this deployment's outbound resilience to the call.</param>
    /// <param name="healthRecorder">Records what the endpoint did, so the availability gate can read it.</param>
    /// <param name="egressGuard">Withholds from the provider whatever this deployment withholds.</param>
    /// <param name="instructionEnvelope">The preamble and postamble every agent here carries.</param>
    /// <param name="loggerFactory">The factory the composed agent and the resilience decorator log through.</param>
    /// <param name="logger">Records the outcome without recording the conversation or what was derived from it.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public ThreadStateAgent(
        ChatGenerationPlan plan,
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
        ILogger<ThreadStateAgent> logger)
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
    /// <remarks>This one is registered only where a deployment turned the derivation on, so it is active by construction.</remarks>
    public bool IsActive => true;

    /// <inheritdoc />
    public async Task<ThreadStateDerivation> DeriveAsync(
        DerivableThread thread,
        MailUserLanguage language,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(thread);

        // Answered before anything is admitted or composed, because it costs nothing and is permanent for this shape of
        // the conversation: a thread past the bound is recorded as too long rather than paid for and cut down.
        if (thread.ExceedsBound)
        {
            ThreadStateEvents.LogTooLarge(this.logger, thread.Revision.MessageCount);

            return ThreadStateDerivation.TooLarge();
        }

        if (thread.Messages.Count is 0)
        {
            return ThreadStateDerivation.Settled([]);
        }

        // Admitted before anything is composed or scanned, so a period this deployment has already spent costs nothing
        // to refuse. The count is what stops this from spending an allowance a question would otherwise have.
        if (!await this.spendLedger.TryAdmitRunAsync(cancellationToken))
        {
            return this.Withhold(ThreadStateWithholding.AllowanceExhausted);
        }

        var turn = Bounded(
            ThreadStateInstructions.ComposeThreadTurn(
                await this.egressGuard.GuardOptionalAsync(
                    SensitiveContentEgressPoint.ChatPrompt,
                    thread.Subject,
                    cancellationToken),
                await this.GuardedMessagesAsync(thread.Messages, cancellationToken)),
            this.plan.MaximumRequestCharacters);

        ChatRequestBounds.Require(
            [new ChatMessage(ChatRole.User, turn)],
            this.plan.MaximumMessagesPerRequest,
            this.plan.MaximumRequestCharacters,
            this.plan.MaximumRequestImageOctets);

        if (await this.AskAsync(turn, language, cancellationToken) is not { } answerText)
        {
            return this.Withhold(ThreadStateWithholding.ProviderUnavailable);
        }

        var entries = ThreadStateReading.Read(answerText, thread.Messages);

        if (entries.Count is 0)
        {
            ThreadStateEvents.LogAnswerUnreadable(this.logger, this.plan.Endpoint.Alias);
        }
        else
        {
            ThreadStateEvents.LogDerived(
                this.logger,
                this.plan.Endpoint.Alias,
                entries.Count,
                thread.Messages.Count);
        }

        return ThreadStateDerivation.Settled(entries);
    }

    /// <summary>Cuts a turn down to what one call may carry, without splitting a character in half.</summary>
    /// <remarks>
    /// A bound rather than a refusal, and it is the same trade the enrichment derivation makes: a refusal here would be
    /// permanent for this shape of the conversation, so an endpoint declared narrower than a long exchange would leave
    /// it outstanding forever. What a bounded turn costs is the tail of the last message, and a statement citing a
    /// message the model saw only part of is still a statement somebody can check. A conversation long enough for that
    /// to matter has already been answered by <see cref="ThreadStateDerivation.TooLarge" /> above.
    /// </remarks>
    private static string Bounded(string turn, int maximumCharacters) =>
        turn.Length <= maximumCharacters
            ? turn
            : MailTextBounds.TruncateAtTextElementBoundary(turn, maximumCharacters);

    /// <summary>Puts every message of the conversation through the egress guard before any of it is composed into a turn.</summary>
    /// <remarks>
    /// The name and the text are somebody's mail and are scanned like every other text this deployment sends: a message
    /// quoting a key a colleague pasted has put that key into the request. The instant and the position are this
    /// deployment's own and carry nothing a sender wrote.
    /// </remarks>
    private async Task<IReadOnlyList<GuardedThreadMessage>> GuardedMessagesAsync(
        IReadOnlyList<DerivableThreadMessage> messages,
        CancellationToken cancellationToken)
    {
        var guarded = new List<GuardedThreadMessage>(messages.Count);

        foreach (var message in messages)
        {
            guarded.Add(new GuardedThreadMessage(
                message.Position,
                await this.egressGuard.GuardOptionalAsync(
                    SensitiveContentEgressPoint.ChatPrompt,
                    message.AuthorDisplayName,
                    cancellationToken),
                message.SentAt,
                await this.egressGuard.GuardAsync(
                    SensitiveContentEgressPoint.ChatPrompt,
                    message.Text,
                    cancellationToken)));
        }

        return guarded;
    }

    /// <summary>Makes the one provider call, answering with nothing where it failed.</summary>
    /// <remarks>
    /// A failure is swallowed here rather than raised because the caller already has an outcome for that case, and the
    /// endpoint's own health record — which the resilience decorator wrote before this returned — is what a
    /// deployment's availability gate reads. A cancellation stays outside, being the caller withdrawing the work rather
    /// than a provider failing to answer.
    /// </remarks>
    private async Task<string?> AskAsync(
        string turn,
        MailUserLanguage language,
        CancellationToken cancellationToken)
    {
        var endpoint = this.plan.Endpoint;

        try
        {
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
            // never reaches the endpoint's circuit, its concurrency budget, or its health record. The run ledger is
            // this derivation's own — one conversation is one run — and the period ledger is the deployment's.
            await using var chatClient = new BudgetedChatClient(
                resilientClient,
                new MailAnsweringRunLedger(this.runBounds),
                this.spendLedger);

            var agent = ThreadStateAgentComposition.Compose(
                chatClient,
                this.plan,
                language,
                this.instructionEnvelope,
                this.loggerFactory);

            var response = await agent.RunAsync(turn, session: null, options: null, cancellationToken);

            return response.Text;
        }
        catch (ChatGenerationFailedException)
        {
            return null;
        }
        catch (MailAnsweringBudgetExhaustedException)
        {
            // The per-derivation ceiling, which a single call reaches only where an operator declared a run smaller
            // than one call. It is a withholding rather than an empty answer for the same reason a spent period is:
            // nothing about the conversation decided it.
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

    private ThreadStateDerivation Withhold(ThreadStateWithholding withholding)
    {
        ThreadStateEvents.LogWithheld(this.logger, this.plan.Endpoint.Alias, withholding);

        return ThreadStateDerivation.Withholding(withholding);
    }
}

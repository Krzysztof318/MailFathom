// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Providers;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Resilience;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Failures;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.ProviderAdapters;

/// <summary>Produces answers by calling the declared chat endpoint.</summary>
/// <remarks>
/// <para>
/// The whole of the single-request path to a chat provider. Everything provider-specific stops in this namespace: the
/// client library, its options, its exceptions, and the two authentication shapes, so a second provider is a new
/// endpoint declaration rather than a change anywhere above.
/// </para>
/// <para>
/// It composes nothing. The conversation arrives built, the model and its parameters arrive validated in the plan, and
/// what this adds is the deadline, the resilience budget, the classification of a failure, the sensitive-content guard
/// every turn passes through, and the refusal to let any prompt or answer reach a log.
/// </para>
/// <para>
/// The client library's namespace is written out at every use rather than imported, because it publishes a
/// <c>ChatMessage</c> and a <c>ChatRole</c> of its own and a reader seeing either bare name would have to work out
/// which one they were looking at. <see cref="ChatConversationMapping" /> is where both sets meet.
/// </para>
/// </remarks>
internal sealed class ProviderChatModelClient : IChatModelClient
{
    /// <summary>Names the registered transport a chat request is sent over.</summary>
    /// <remarks>
    /// Declared by the consumer rather than by the registration, because a name resolved through
    /// <see cref="IHttpClientFactory" /> is a string either side can get wrong in silence: asking for one that was
    /// never registered yields a client with no bounds and no handlers rather than a failure. One constant, referenced
    /// from both, is what makes that a compile-time agreement.
    /// </remarks>
    internal const string TransportName = "mailfathom.chat-provider";

    private readonly ChatGenerationPlan plan;
    private readonly IProviderEndpointCredentialSource credentialSource;
    private readonly OpenAiCompatibleClientFactory clientFactory;
    private readonly IHttpClientFactory transportFactory;
    private readonly IOutboundOperationRunner operationRunner;
    private readonly IAiProviderHealthRecorder healthRecorder;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly ILogger<ProviderChatModelClient> logger;

    /// <summary>Initializes a client over the declared endpoint, its credentials, its transport, and its resilience budget.</summary>
    /// <param name="plan">The validated declaration: which endpoint answers and with which parameters.</param>
    /// <param name="credentialSource">Resolves what a request presents to the endpoint.</param>
    /// <param name="clientFactory">Opens a provider client over the endpoint.</param>
    /// <param name="transportFactory">Opens the transport a request is sent over, one per attempt.</param>
    /// <param name="operationRunner">Applies the provider resilience budget.</param>
    /// <param name="healthRecorder">Records what each call established about the provider.</param>
    /// <param name="egressGuard">Scans every turn before it is sent, where this deployment scans anything.</param>
    /// <param name="logger">Records the outcome without recording any prompt, answer, or credential.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public ProviderChatModelClient(
        ChatGenerationPlan plan,
        IProviderEndpointCredentialSource credentialSource,
        OpenAiCompatibleClientFactory clientFactory,
        IHttpClientFactory transportFactory,
        IOutboundOperationRunner operationRunner,
        IAiProviderHealthRecorder healthRecorder,
        SensitiveContentEgressGuard egressGuard,
        ILogger<ProviderChatModelClient> logger)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(credentialSource);
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(transportFactory);
        ArgumentNullException.ThrowIfNull(operationRunner);
        ArgumentNullException.ThrowIfNull(healthRecorder);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(logger);

        this.plan = plan;
        this.credentialSource = credentialSource;
        this.clientFactory = clientFactory;
        this.transportFactory = transportFactory;
        this.operationRunner = operationRunner;
        this.healthRecorder = healthRecorder;
        this.egressGuard = egressGuard;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<ChatAnswer> AnswerAsync(
        IReadOnlyList<ChatMessage> conversation,
        CancellationToken cancellationToken)
    {
        ChatRequestBounds.Require(
            conversation,
            this.plan.MaximumMessagesPerRequest,
            this.plan.MaximumRequestCharacters);

        var guarded = await this.GuardedAsync(conversation, cancellationToken);

        try
        {
            var answer = await this.RequestAnswerAsync(guarded, cancellationToken);

            this.healthRecorder.RecordServed(AiProviderRole.Chat);

            return answer;
        }
        catch (ChatGenerationFailedException failure)
        {
            ChatProviderEvents.LogCallFailed(this.logger, this.plan.Endpoint.Alias, failure.Failure);

            this.RecordFailure(failure);

            throw;
        }
    }

    /// <summary>Scans every turn of a conversation before any of it is sent to a third party.</summary>
    /// <remarks>
    /// <para>
    /// Applied here rather than inside the send, so one scan covers one call however many attempts the resilience
    /// pipeline makes of it, and so a scanner that cannot answer refuses the call as itself instead of arriving at the
    /// pipeline as a fault of the provider's.
    /// </para>
    /// <para>
    /// Every turn is scanned rather than only the ones a caller composed from mail, because a conversation reaching
    /// this port is already built and nothing here can tell which turn a mailbox reached. That includes the one a
    /// person typed: a question is text somebody wrote into a client, and it leaves this deployment as completely as an
    /// extract does.
    /// </para>
    /// <para>
    /// The bounds above run first and are checked against what the caller composed. Redaction can only make a turn
    /// longer — a placeholder is wider than most of what it replaces — so a conversation admitted at the boundary may
    /// be sent slightly wider than the boundary allows; cutting it afterwards would drop text a scan had just made
    /// safe, which is the wrong thing to lose.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<ChatMessage>> GuardedAsync(
        IReadOnlyList<ChatMessage> conversation,
        CancellationToken cancellationToken)
    {
        if (!this.egressGuard.IsActive)
        {
            return conversation;
        }

        var guarded = new List<ChatMessage>(conversation.Count);

        foreach (var turn in conversation)
        {
            guarded.Add(turn with
            {
                Text = await this.egressGuard.GuardAsync(
                    SensitiveContentEgressPoint.ChatPrompt,
                    turn.Text,
                    cancellationToken),
            });
        }

        return guarded;
    }

    private async Task<ChatAnswer> RequestAnswerAsync(
        IReadOnlyList<ChatMessage> conversation,
        CancellationToken cancellationToken)
    {
        var endpoint = this.plan.Endpoint;

        // Resolved per request and released with it, so a rotated key is picked up by the next call and the material
        // exists for one request rather than for process uptime.
        using var credential = await this.credentialSource.ResolveAsync(endpoint.Alias, cancellationToken);

        Microsoft.Extensions.AI.ChatResponse response;
        try
        {
            // Keyed by the endpoint alias, which is the deployment's own name for it, so nothing personal reaches
            // resilience telemetry and a chat outage opens a circuit of its own rather than the embedding provider's.
            response = await this.operationRunner.RunAsync(
                OutboundDependency.AiProviderInvocation,
                endpoint.Alias,
                attemptToken => this.SendAsync(credential, conversation, attemptToken),
                cancellationToken);
        }
        catch (MailFathomException rejection)
            when (rejection.ErrorCode == MailFathomErrorCode.OutboundDependencyUnavailable)
        {
            // The pipeline declined to call the endpoint at all — its circuit is open, or its concurrency budget is
            // spent. Recognized by code rather than by type, which is what a stable error code is for: the resilience
            // library and the exception it raises belong to another adapter boundary that this one may not reference.
            throw new ChatGenerationFailedException(
                endpoint.Alias,
                ChatGenerationFailure.TransportFaulted,
                rejection);
        }

        return this.MapAnswer(response);
    }

    /// <summary>Sends one request and returns exactly what the provider answered.</summary>
    /// <remarks>
    /// The transport is opened per attempt and released with it, which is what keeps the connection bounds in the
    /// registration rather than in a client held across a process: the factory retires a handler chain on its own
    /// schedule, so an endpoint that has moved is reached at its new address by the next attempt. Inside a retry a
    /// per-attempt client also costs nothing.
    /// </remarks>
    private async Task<Microsoft.Extensions.AI.ChatResponse> SendAsync(
        ProviderEndpointCredential credential,
        IReadOnlyList<ChatMessage> conversation,
        CancellationToken cancellationToken)
    {
        var endpoint = this.plan.Endpoint;

        // The deadline is this deployment's and is applied here rather than left to the client, so one attempt is
        // bounded whichever provider library is underneath and whatever it defaults to.
        using var attemptDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptDeadline.CancelAfter(this.plan.RequestTimeout);

        using var transport = this.transportFactory.CreateClient(TransportName);
        using var client = this.clientFactory.OpenChatClient(endpoint, credential, transport);

        var options = ChatGenerationParameterMapping.ToChatOptions(this.plan);

        try
        {
            return await client.GetResponseAsync(
                ChatConversationMapping.ToProviderConversation(conversation),
                options,
                attemptDeadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The caller did not cancel, so the deadline above did. Reporting it as a cancellation would tell the
            // pipeline that this system stopped the work, and a timeout is the dependency failing to answer.
            throw new ChatGenerationFailedException(endpoint.Alias, ChatGenerationFailure.RequestTimedOut);
        }
        catch (Exception failure) when (ProviderCallFailureClassification.Classify(failure) is { } classified)
        {
            throw new ChatGenerationFailedException(endpoint.Alias, ToChatFailure(classified), failure);
        }
    }

    /// <summary>Turns the provider's answer into the one this port publishes, or refuses it.</summary>
    /// <remarks>
    /// An answer with no text is refused rather than passed on, because an empty string reaching a caller reads as a
    /// model that had nothing to say rather than as a call that produced nothing. Every other shape is an answer: a
    /// truncation and a content filter are reported through the stop reason, which is what keeps either from ever being
    /// repeated as though it were a transport fault.
    /// </remarks>
    private ChatAnswer MapAnswer(Microsoft.Extensions.AI.ChatResponse response)
    {
        var endpoint = this.plan.Endpoint;
        var stop = ToGenerationStop(response.FinishReason);

        if (string.IsNullOrWhiteSpace(response.Text))
        {
            // Logged before the failure is raised, because the failure names only that no text arrived. A provider
            // whose safety system withheld the whole answer produced no text either, and an operator reading "answer
            // empty" alone would go looking at the declaration for something that is working correctly.
            ChatProviderEvents.LogAnswerCutShort(this.logger, endpoint.Alias, stop);

            throw new ChatGenerationFailedException(endpoint.Alias, ChatGenerationFailure.AnswerEmpty);
        }

        var usage = response.Usage is { } reported
            ? new ChatTokenUsage(reported.InputTokenCount ?? 0, reported.OutputTokenCount ?? 0)
            : null;

        ChatProviderEvents.LogAnswered(
            this.logger,
            endpoint.Alias,
            usage?.InputTokens ?? 0,
            usage?.OutputTokens ?? 0,
            stop);

        if (stop is ChatGenerationStop.OutputLimitReached or ChatGenerationStop.ContentFiltered)
        {
            ChatProviderEvents.LogAnswerCutShort(this.logger, endpoint.Alias, stop);
        }

        return new ChatAnswer(response.Text, stop, usage);
    }

    /// <summary>Records what the failure established about the provider, at the granularity an operator acts on.</summary>
    /// <remarks>
    /// <para>
    /// The health state answers whether the provider is usable, which is a narrower question than whether the call
    /// produced an answer. An endpoint that took the request, authenticated it, ran the model, and came back with no
    /// text is a working endpoint: the credential, the address, and the routed model were all right, so reporting it as
    /// something an operator has to fix would send them after a deployment that has nothing wrong with it.
    /// </para>
    /// <para>
    /// The remaining split follows the exception's own <see cref="ChatGenerationFailedException.IsWorthRepeating" />,
    /// so the health state and the resilience pipeline can never disagree about whether waiting is the answer.
    /// </para>
    /// </remarks>
    private void RecordFailure(ChatGenerationFailedException failure)
    {
        if (failure.Failure is ChatGenerationFailure.AnswerEmpty)
        {
            this.healthRecorder.RecordServed(AiProviderRole.Chat);

            return;
        }

        if (failure.IsWorthRepeating)
        {
            this.healthRecorder.RecordUnavailable(AiProviderRole.Chat);

            return;
        }

        this.healthRecorder.RecordMisconfigured(AiProviderRole.Chat);
    }

    /// <summary>Reads the provider's finish reason into the one this port publishes.</summary>
    /// <remarks>
    /// A reason outside the three that mean something here, and an answer that reports none at all, both become
    /// <see cref="ChatGenerationStop.Unreported" />: neither says the model finished, and claiming it did would state
    /// something the provider did not. A tool call falls here too, because this boundary offers no tools, so a provider
    /// answering with one has answered something nothing asked for.
    /// <para>
    /// Which reason arrives depends on the API the endpoint declared, and the null branch is where that shows. Chat
    /// completions always names one — the client refuses a <c>finish_reason</c> of JSON null and substitutes its own
    /// default for an absent one — while the responses API reports an outcome instead: a response that stopped early
    /// says why, and one that simply finished names nothing and arrives here as null. Reporting that as
    /// <see cref="ChatGenerationStop.Completed" /> would claim the model finished on a surface that never said so, which
    /// is the same claim this method refuses to make everywhere else.
    /// </para>
    /// </remarks>
    private static ChatGenerationStop ToGenerationStop(Microsoft.Extensions.AI.ChatFinishReason? finishReason) =>
        finishReason switch
        {
            null => ChatGenerationStop.Unreported,
            { } reason when reason == Microsoft.Extensions.AI.ChatFinishReason.Stop => ChatGenerationStop.Completed,
            { } reason when reason == Microsoft.Extensions.AI.ChatFinishReason.Length =>
                ChatGenerationStop.OutputLimitReached,
            { } reason when reason == Microsoft.Extensions.AI.ChatFinishReason.ContentFilter =>
                ChatGenerationStop.ContentFiltered,
            _ => ChatGenerationStop.Unreported,
        };

    private static ChatGenerationFailure ToChatFailure(ProviderCallFailure failure) => failure switch
    {
        ProviderCallFailure.CredentialRejected => ChatGenerationFailure.CredentialRejected,
        ProviderCallFailure.RateLimited => ChatGenerationFailure.RateLimited,
        ProviderCallFailure.RequestTimedOut => ChatGenerationFailure.RequestTimedOut,
        ProviderCallFailure.RequestRefused => ChatGenerationFailure.RequestRefused,
        _ => ChatGenerationFailure.TransportFaulted,
    };
}

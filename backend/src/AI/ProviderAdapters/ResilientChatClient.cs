// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Resilience;
using MailFathom.Domain.Failures;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.ProviderAdapters;

/// <summary>Puts this deployment's deadline, resilience budget, failure classification, and health reporting around each chat call a run makes.</summary>
/// <remarks>
/// <para>
/// A decorator rather than a call site, because an agent run is several provider calls rather than one and the framework
/// makes them: it holds one client for the length of the run and calls it once per turn of the tool loop. The bounds
/// therefore have to sit inside the client, where every call passes through them, instead of around a single request.
/// </para>
/// <para>
/// It is the reason an agent run is governed at all. Without it a run would hold whatever timeout the provider library
/// defaults to, retry inside no budget, and tell the health state nothing — three properties every other outbound call in
/// this system has.
/// </para>
/// <para>
/// The transport underneath is opened once for the run rather than per attempt, which is the one thing this shape gives
/// up against a single bounded request: the framework holds the client, so a retried call reuses the handler chain the
/// run began with. A run is short and an endpoint that moves mid-run is reached at its new address by the next run.
/// </para>
/// </remarks>
internal sealed class ResilientChatClient : Microsoft.Extensions.AI.DelegatingChatClient
{
    private readonly ChatEndpoint endpoint;
    private readonly TimeSpan requestTimeout;
    private readonly IOutboundOperationRunner operationRunner;
    private readonly IAiProviderHealthRecorder healthRecorder;
    private readonly ILogger logger;

    /// <summary>Initializes the decorator over the client one run sends through.</summary>
    /// <param name="innerClient">The provider client every call is delegated to.</param>
    /// <param name="endpoint">The declared endpoint, whose alias keys the budget and names the failure.</param>
    /// <param name="requestTimeout">The time one call may take before it is abandoned.</param>
    /// <param name="operationRunner">Applies the provider resilience budget.</param>
    /// <param name="healthRecorder">Records what each call established about the provider.</param>
    /// <param name="logger">Records the outcome without recording any prompt or answer.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal ResilientChatClient(
        Microsoft.Extensions.AI.IChatClient innerClient,
        ChatEndpoint endpoint,
        TimeSpan requestTimeout,
        IOutboundOperationRunner operationRunner,
        IAiProviderHealthRecorder healthRecorder,
        ILogger logger)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(operationRunner);
        ArgumentNullException.ThrowIfNull(healthRecorder);
        ArgumentNullException.ThrowIfNull(logger);

        this.endpoint = endpoint;
        this.requestTimeout = requestTimeout;
        this.operationRunner = operationRunner;
        this.healthRecorder = healthRecorder;
        this.logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="ChatGenerationFailedException">Thrown when the call produced no response, naming which kind of failure ended it.</exception>
    public override async Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await this.SendUnderBudgetAsync(messages, options, cancellationToken);

            this.healthRecorder.RecordServed(AiProviderRole.Chat);

            return response;
        }
        catch (ChatGenerationFailedException failure)
        {
            ChatProviderEvents.LogCallFailed(this.logger, this.endpoint.Alias, failure.Failure);

            this.RecordFailure(failure);

            throw;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Answering as it arrives is a shape nothing here needs: a run produces one answer to one question, and a streaming
    /// call would carry its own cancellation and partial-answer semantics through the budget above. Refusing it outright
    /// is what keeps a caller from reaching the provider along a path none of these bounds apply to.
    /// </remarks>
    /// <exception cref="NotSupportedException">Always thrown, because no caller streams and an unbounded path must not exist for one to find.</exception>
    public override IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Streaming a chat response is not supported: the deadline and resilience budget this deployment applies are written for a call that returns one answer.");

    /// <summary>Runs one call under the resilience budget, translating a pipeline refusal into this system's failure.</summary>
    private async Task<Microsoft.Extensions.AI.ChatResponse> SendUnderBudgetAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options,
        CancellationToken cancellationToken)
    {
        // Materialized once, because the pipeline may make several attempts and a sequence built by the framework is not
        // promised to survive a second enumeration.
        var conversation = messages as IReadOnlyList<Microsoft.Extensions.AI.ChatMessage> ?? [.. messages];

        try
        {
            // Keyed by the endpoint alias, which is the deployment's own name for it, so nothing personal reaches
            // resilience telemetry and a chat outage opens a circuit of its own rather than the embedding provider's.
            return await this.operationRunner.RunAsync(
                OutboundDependency.AiProviderInvocation,
                this.endpoint.Alias,
                attemptToken => this.SendAsync(conversation, options, attemptToken),
                cancellationToken);
        }
        catch (MailFathomException rejection) when (ChatCallFailureMapping.IsEndpointNotCalled(rejection))
        {
            throw ChatCallFailureMapping.ToEndpointNotCalledFailure(rejection, this.endpoint.Alias);
        }
    }

    /// <summary>Sends one attempt under this deployment's own deadline.</summary>
    private async Task<Microsoft.Extensions.AI.ChatResponse> SendAsync(
        IReadOnlyList<Microsoft.Extensions.AI.ChatMessage> conversation,
        Microsoft.Extensions.AI.ChatOptions? options,
        CancellationToken cancellationToken)
    {
        // The deadline is this deployment's and is applied here rather than left to the client, so one attempt is
        // bounded whichever provider library is underneath and whatever it defaults to.
        using var attemptDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptDeadline.CancelAfter(this.requestTimeout);

        try
        {
            return await this.InnerClient.GetResponseAsync(conversation, options, attemptDeadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The caller did not cancel, so the deadline above did. Reporting it as a cancellation would tell the
            // pipeline that this system stopped the work, and a timeout is the dependency failing to answer.
            throw new ChatGenerationFailedException(this.endpoint.Alias, ChatGenerationFailure.RequestTimedOut);
        }
        catch (Exception failure) when (ProviderCallFailureClassification.Classify(failure) is { } classified)
        {
            throw new ChatGenerationFailedException(
                this.endpoint.Alias,
                ChatCallFailureMapping.ToChatFailure(classified),
                failure);
        }
    }

    /// <summary>Records what the failure established about the provider, at the granularity an operator acts on.</summary>
    /// <remarks>
    /// The split follows the exception's own <see cref="ChatGenerationFailedException.IsWorthRepeating" />, so the health
    /// state and the resilience pipeline can never disagree about whether waiting is the answer.
    /// </remarks>
    private void RecordFailure(ChatGenerationFailedException failure)
    {
        if (failure.IsWorthRepeating)
        {
            this.healthRecorder.RecordUnavailable(AiProviderRole.Chat);

            return;
        }

        this.healthRecorder.RecordMisconfigured(AiProviderRole.Chat);
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Resilience;
using MailFathom.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using xRetry.v3;
using Xunit;

namespace MailFathom.IntegrationTests.ProviderAdapters;

/// <summary>
/// Proves the chat adapter against a real provider: that it speaks the protocol, authenticates, classifies a real
/// refusal, and returns an answer in the shape the port publishes.
/// </summary>
/// <remarks>
/// <para>
/// Two tests, deliberately, and the same two the embedding adapter has, because the same four things are what only a
/// real provider can establish. Everything else this boundary needs proved — the request bounds, the mapping of a
/// conversation, the stop reasons, the budget, the health recording — is provable against a substituted client at zero
/// provider cost and belongs there.
/// </para>
/// <para>
/// Skipped unless somebody asks, through the one switch every provider-calling test in this suite shares. See
/// <see cref="AiProviderContractRun" />.
/// </para>
/// <para>
/// The class joins no collection, and each test is retried, for the reasons
/// <see cref="EmbeddingProviderContractTests" /> gives at length: it needs neither the orchestrated database nor the
/// orchestrated mailbox, and a <c>429</c> or a <c>529</c> from the provider says nothing about any of the four claims
/// above.
/// </para>
/// </remarks>
public sealed class ChatProviderContractTests
{
    /// <summary>How many times a test is run before its failure is reported.</summary>
    /// <remarks>Attempts rather than retries, despite the parameter's name: the runner counts the first run among them.</remarks>
    private const int MaxAttempts = 3;

    /// <summary>How long to wait before running a test again.</summary>
    /// <remarks>Sized for the two answers that clear on their own within seconds — a concurrency or burst limit, and a momentary overload.</remarks>
    private const int DelayBetweenAttemptsMs = 5000;

    /// <summary>Gets whether a provider-contract run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool ProviderContractTestsRequested => AiProviderContractRun.Requested;

    [RetryFact(
        MaxAttempts,
        DelayBetweenAttemptsMs,
        Skip = AiProviderContractRun.SkipReason,
        SkipUnless = nameof(ProviderContractTestsRequested))]
    public async Task AnswerAsync_AgainstTheRealProvider_ReturnsAnAnswerWithinTheDeclaredBudget()
    {
        // Arrange
        var plan = ChatProviderContractSettings.Plan();
        using var composition = Compose(ChatProviderContractSettings.ApiKey());
        var client = ClientOver(plan, composition);

        // Act
        var answer = await client.AnswerAsync(
            [
                new ChatMessage(ChatRole.System, "Answer with a single word and nothing else."),
                new ChatMessage(ChatRole.User, "Which city is the Helsinki office in?"),
            ],
            TestContext.Current.CancellationToken);

        // Assert
        // The text is never asserted against, only established to exist. What a model says is the model's, and a test
        // that expected a particular word would fail on a correct answer phrased differently.
        Assert.False(string.IsNullOrWhiteSpace(answer.Text));

        // Both stops are a real answer. A model that spends its budget reasoning reaches the ceiling on a question this
        // short, and the text before the cut is still what the adapter had to return.
        ChatGenerationStop[] answered = [ChatGenerationStop.Completed, ChatGenerationStop.OutputLimitReached];
        Assert.Contains(answer.Stop, answered);

        // Usage is what a spend ceiling is measured in, so a provider that reports none is worth knowing about here
        // rather than when a deployment's budget silently stops counting.
        Assert.NotNull(answer.Usage);
        Assert.InRange(answer.Usage.OutputTokens, 1L, (long)plan.MaximumOutputTokens);
    }

    /// <summary>
    /// The one failure worth a paid call to prove: the classification the adapter derives has to match what the
    /// provider actually answers to a credential it does not accept, and no unit test can establish that.
    /// </summary>
    [RetryFact(
        MaxAttempts,
        DelayBetweenAttemptsMs,
        Skip = AiProviderContractRun.SkipReason,
        SkipUnless = nameof(ProviderContractTestsRequested))]
    public async Task AnswerAsync_WithACredentialTheProviderRefuses_IsClassifiedAsSuch()
    {
        // Arrange
        var plan = ChatProviderContractSettings.Plan();
        using var composition = Compose("mailfathom-contract-test-key-the-provider-will-refuse");
        var client = ClientOver(plan, composition);

        // Act
        var failure = await Assert.ThrowsAsync<ChatGenerationFailedException>(() =>
            client.AnswerAsync(
                [new ChatMessage(ChatRole.User, "Which city is the Helsinki office in?")],
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(ChatGenerationFailure.CredentialRejected, failure.Failure);
        Assert.False(failure.IsWorthRepeating);
    }

    private static ServiceProvider Compose(string apiKey)
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddSingleton<IProviderEndpointCredentialSource>(new FixedChatCredentialSource(apiKey));

        // The real tracker rather than a double, because it is what the host registers and it reaches nothing: the
        // adapter's only use of it is to record an outcome, so a run against the real provider exercises the same
        // recording path a deployment does at no additional cost.
        services.AddLogging();
        services.AddSingleton<IAiProviderHealthRecorder>(provider => new AiProviderHealthTracker(
            TimeProvider.System,
            provider.GetRequiredService<ILogger<AiProviderHealthTracker>>()));

        return services.BuildServiceProvider();
    }

    /// <summary>Composes the adapter over the real transport, with the resilience budget passed straight through.</summary>
    /// <remarks>
    /// A pass-through runner rather than the configured pipeline, for the reason the embedding contract states: what a
    /// pipeline adds is covered where it is implemented, and wrapping one here would turn a refused credential into
    /// several paid attempts of the same refusal.
    /// </remarks>
    private static ProviderChatModelClient ClientOver(ChatGenerationPlan plan, IServiceProvider composition) =>
        new(
            plan,
            composition.GetRequiredService<IProviderEndpointCredentialSource>(),
            new OpenAiCompatibleClientFactory(),
            composition.GetRequiredService<IHttpClientFactory>(),
            new PassThroughOutboundOperationRunner(),
            composition.GetRequiredService<IAiProviderHealthRecorder>(),
            NullLogger<ProviderChatModelClient>.Instance);

    private sealed class FixedChatCredentialSource(string apiKey) : IProviderEndpointCredentialSource
    {
        public Task<ProviderEndpointCredential> ResolveAsync(
            string endpointAlias,
            CancellationToken cancellationToken) =>
            Task.FromResult(ProviderEndpointCredential.FromApiKey(apiKey, resolvedMaterial: null));
    }

    private sealed class PassThroughOutboundOperationRunner : IOutboundOperationRunner
    {
        public Task<TResult> RunAsync<TResult>(
            OutboundDependency dependency,
            string remoteInstance,
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken) =>
            operation(cancellationToken);
    }
}

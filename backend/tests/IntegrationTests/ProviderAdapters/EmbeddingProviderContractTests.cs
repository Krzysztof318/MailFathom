// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Embeddings;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Resilience;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using xRetry.v3;
using Xunit;

namespace MailFathom.IntegrationTests.ProviderAdapters;

/// <summary>
/// Proves the provider adapter against a real provider: that it speaks the protocol, authenticates, classifies a real
/// refusal, and returns the width the profile claims.
/// </summary>
/// <remarks>
/// <para>
/// Two tests, deliberately, and not a suite. Everything else this feature needs proved — the dimension check, the
/// uniqueness constraint, the per-profile index, the idempotent write, two generations coexisting, the bounded
/// removal — is provable against a real database and the in-repository generator at zero provider cost, and belongs
/// there. What only a real provider can establish is exactly what is below. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>.
/// </para>
/// <para>
/// Skipped unless somebody asks. The condition reads an environment variable nothing sets, so a developer's run and an
/// ordinary pipeline run both spend nothing; the `Integration tests` workflow turns it on through an input that
/// defaults to off and supplies the credential with it.
/// </para>
/// <para>
/// The class joins no collection. It needs neither the orchestrated database nor the orchestrated mailbox, so
/// serializing it against them would make an already expensive suite slower for nothing.
/// </para>
/// <para>
/// Each test is retried, which is a licence this suite grants nowhere else and which `backend/tests/AGENTS.md` states as a
/// rule. What it answers here is that a `429` or a `529` from the provider says nothing about any of the four claims
/// above, so reporting one as a failure would say the adapter is wrong when the provider was busy.
/// </para>
/// </remarks>
public sealed class EmbeddingProviderContractTests
{
    /// <summary>How many times a test is run before its failure is reported.</summary>
    /// <remarks>
    /// Attempts rather than retries, despite the parameter's name: the runner counts the first run among them, so three
    /// here is one call and two more after a transient answer.
    /// </remarks>
    private const int MaxAttempts = 3;

    /// <summary>How long to wait before running a test again.</summary>
    /// <remarks>
    /// A rate limit clears on the provider's own schedule rather than on the caller's, so an immediate second attempt
    /// mostly buys a second refusal. This is sized for the two answers that clear on their own within seconds — a
    /// concurrency or burst limit, and a momentary overload — and deliberately not for a per-minute quota that is
    /// genuinely exhausted, which no wait this suite should hold would outlast.
    /// </remarks>
    private const int DelayBetweenAttemptsMs = 5000;

    /// <summary>Gets whether a provider-contract run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool ProviderContractTestsRequested =>
        AiProviderContractRun.Requested;

    [RetryFact(
        MaxAttempts,
        DelayBetweenAttemptsMs,
        Skip = AiProviderContractRun.SkipReason,
        SkipUnless = nameof(ProviderContractTestsRequested))]
    public async Task GenerateAsync_AgainstTheRealProvider_AnswersInTheDeclaredSpace()
    {
        // Arrange
        var plan = EmbeddingProviderContractSettings.Plan();
        using var composition = Compose(EmbeddingProviderContractSettings.ApiKey());
        var generator = GeneratorOver(plan, composition);

        // Act
        var vectors = await generator.GenerateAsync(
            ["A quarterly invoice for the Helsinki office.", "A delivery notice for the same order."],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, vectors.Count);
        Assert.All(vectors, vector => Assert.Equal(plan.Identity.Dimension, vector.Dimension));
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
    public async Task GenerateAsync_WithACredentialTheProviderRefuses_IsClassifiedAsSuch()
    {
        // Arrange
        var plan = EmbeddingProviderContractSettings.Plan();
        using var composition = Compose("mailfathom-contract-test-key-the-provider-will-refuse");
        var generator = GeneratorOver(plan, composition);

        // Act
        var failure = await Assert.ThrowsAsync<EmbeddingGenerationFailedException>(() =>
            generator.GenerateAsync(["A quarterly invoice."], TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(EmbeddingGenerationFailure.CredentialRejected, failure.Failure);
        Assert.False(failure.IsWorthRepeating);
    }

    private static ServiceProvider Compose(string apiKey)
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddSingleton<IProviderEndpointCredentialSource>(new FixedEmbeddingCredentialSource(apiKey));

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
    /// A pass-through runner rather than the configured pipeline, because what a pipeline adds — bounded attempts,
    /// a circuit, a concurrency limit — is covered where it is implemented, and wrapping one here would turn a refused
    /// credential into several paid attempts of the same refusal.
    /// <para>
    /// The retry on each test is not that pipeline arriving by another route. It sits above the assertion rather than
    /// under it, so a test that passes still makes exactly one call and the classification is still read from the
    /// provider's first answer; only a test that already failed is run again.
    /// </para>
    /// </remarks>
    private static ProviderTextEmbeddingGenerator GeneratorOver(
        EmbeddingGenerationPlan plan,
        IServiceProvider composition) =>
        new(
            plan,
            composition.GetRequiredService<IProviderEndpointCredentialSource>(),
            new OpenAiCompatibleClientFactory(),
            composition.GetRequiredService<IHttpClientFactory>(),
            new PassThroughOutboundOperationRunner(),
            composition.GetRequiredService<IAiProviderHealthRecorder>(),
            SensitiveContentEgressGuards.Inactive(),
            NullLogger<ProviderTextEmbeddingGenerator>.Instance);

    private sealed class FixedEmbeddingCredentialSource(string apiKey) : IProviderEndpointCredentialSource
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

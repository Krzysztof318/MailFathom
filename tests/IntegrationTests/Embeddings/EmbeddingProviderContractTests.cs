// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Embeddings;
using MailFathom.AI.ProviderAdapters;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailFathom.IntegrationTests.Embeddings;

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
/// </remarks>
public sealed class EmbeddingProviderContractTests
{
    /// <summary>Gets whether a provider-contract run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool ProviderContractTestsRequested =>
        EmbeddingProviderContractSettings.ProviderContractTestsRequested;

    [Fact(
        Skip = EmbeddingProviderContractSettings.SkipReason,
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
    [Fact(
        Skip = EmbeddingProviderContractSettings.SkipReason,
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
        services.AddSingleton<IEmbeddingCredentialSource>(new FixedEmbeddingCredentialSource(apiKey));

        return services.BuildServiceProvider();
    }

    /// <summary>Composes the adapter over the real transport, with the resilience budget passed straight through.</summary>
    /// <remarks>
    /// A pass-through runner rather than the configured pipeline, because what a pipeline adds — bounded attempts,
    /// a circuit, a concurrency limit — is covered where it is implemented, and wrapping one here would turn a refused
    /// credential into several paid attempts of the same refusal.
    /// </remarks>
    private static ProviderTextEmbeddingGenerator GeneratorOver(
        EmbeddingGenerationPlan plan,
        IServiceProvider composition) =>
        new(
            plan,
            composition.GetRequiredService<IEmbeddingCredentialSource>(),
            new OpenAiCompatibleEmbeddingClientFactory(),
            composition.GetRequiredService<IHttpClientFactory>(),
            new PassThroughOutboundOperationRunner(),
            NullLogger<ProviderTextEmbeddingGenerator>.Instance);

    private sealed class FixedEmbeddingCredentialSource(string apiKey) : IEmbeddingCredentialSource
    {
        public Task<EmbeddingEndpointCredential> ResolveAsync(
            string endpointAlias,
            CancellationToken cancellationToken) =>
            Task.FromResult(EmbeddingEndpointCredential.FromApiKey(apiKey, resolvedMaterial: null));
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

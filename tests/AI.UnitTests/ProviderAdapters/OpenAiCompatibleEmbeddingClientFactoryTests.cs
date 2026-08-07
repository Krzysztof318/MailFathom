// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Embeddings;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.AI.UnitTests.ProviderAdapters;

/// <summary>Covers the client construction that serves both providers, over both authentication shapes.</summary>
/// <remarks>
/// Opening a client contacts nothing: a request is what reaches an endpoint, and a Microsoft Entra credential fetches
/// its token at that point rather than at construction. What these tests establish is that one construction covers a
/// first-party endpoint and a cloud deployment of the same model, whichever of the two credential shapes it holds.
/// </remarks>
public sealed class OpenAiCompatibleEmbeddingClientFactoryTests
{
    private readonly OpenAiCompatibleEmbeddingClientFactory factory = new();

    [Fact]
    public void Open_AnEndpointAuthenticatedWithAKey_OpensAGenerator()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
        using var transport = new HttpClient(handler, disposeHandler: false);
        using var credential = EmbeddingEndpointCredential.FromApiKey("a-resolved-key", resolvedMaterial: null);

        // Act
        using var generator = this.factory.Open(EmbeddingDeclarations.Endpoint(), credential, transport);

        // Assert
        Assert.NotNull(generator);
    }

    /// <summary>The shape a deployment holds where there is no key to provision at all.</summary>
    [Fact]
    public void Open_AnEndpointAuthenticatedWithAnEntraCredential_OpensAGenerator()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
        using var transport = new HttpClient(handler, disposeHandler: false);
        using var credential = EmbeddingEndpointCredential.FromEntra(
            new EntraCredentialDeclaration(
                EmbeddingEndpointCredentialKind.ManagedIdentity,
                "https://ai.example.invalid/.default",
                TenantId: null,
                ClientId: null,
                ClientSecret: null,
                CertificatePath: null,
                CertificatePassword: null),
            resolvedMaterial: null);

        var endpoint = EmbeddingDeclarations.Endpoint(
            "cloud-deployment",
            address: "https://resource.cloud.invalid/openai/v1/",
            routedModelName: "embeddings-small");

        // Act
        using var generator = this.factory.Open(endpoint, credential, transport);

        // Assert
        Assert.NotNull(generator);
    }

    /// <summary>An endpoint with no address of its own is the provider's first-party API at the library's default.</summary>
    [Fact]
    public void Open_AnEndpointWithNoAddress_OpensAGeneratorAtTheProviderDefault()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
        using var transport = new HttpClient(handler, disposeHandler: false);
        using var credential = EmbeddingEndpointCredential.FromApiKey("a-resolved-key", resolvedMaterial: null);

        // Act
        using var generator = this.factory.Open(
            EmbeddingDeclarations.Endpoint(address: null),
            credential,
            transport);

        // Assert
        Assert.NotNull(generator);
    }
}

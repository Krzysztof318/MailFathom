// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using MailFathom.AI.Chat;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
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
public sealed class OpenAiCompatibleClientFactoryTests
{
    private readonly OpenAiCompatibleClientFactory factory = new();

    [Fact]
    public void OpenEmbeddingGenerator_AnEndpointAuthenticatedWithAKey_OpensAGenerator()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
        using var transport = new HttpClient(handler, disposeHandler: false);
        using var credential = ProviderEndpointCredential.FromApiKey("a-resolved-key", resolvedMaterial: null);

        // Act
        using var generator = this.factory.OpenEmbeddingGenerator(EmbeddingDeclarations.Endpoint(), credential, transport);

        // Assert
        Assert.NotNull(generator);
    }

    /// <summary>The shape a deployment holds where there is no key to provision at all.</summary>
    [Fact]
    public void OpenEmbeddingGenerator_AnEndpointAuthenticatedWithAnEntraCredential_OpensAGenerator()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
        using var transport = new HttpClient(handler, disposeHandler: false);
        using var credential = ProviderEndpointCredential.FromEntra(
            new EntraCredentialDeclaration(
                ProviderEndpointCredentialKind.ManagedIdentity,
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
        using var generator = this.factory.OpenEmbeddingGenerator(endpoint, credential, transport);

        // Assert
        Assert.NotNull(generator);
    }

    /// <summary>The shape of a model server the operator runs themselves, which asks for no credential at all.</summary>
    [Fact]
    public void OpenEmbeddingGenerator_AnEndpointNeedingNoCredential_OpensAGenerator()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
        using var transport = new HttpClient(handler, disposeHandler: false);
        using var credential = ProviderEndpointCredential.Unauthenticated();

        var endpoint = EmbeddingDeclarations.Endpoint("local-server", address: "http://model-server:8000/v1");

        // Act
        using var generator = this.factory.OpenEmbeddingGenerator(endpoint, credential, transport);

        // Assert
        Assert.NotNull(generator);
    }

    /// <summary>
    /// What "no credential" has to mean on the wire. The client library takes either a key or an authentication policy,
    /// so the shape with neither is a policy that adds nothing — and a placeholder key put there instead would send an
    /// authorization header the operator never wrote, which is the failure this proves does not happen.
    /// </summary>
    [Fact]
    public async Task OpenEmbeddingGenerator_AnEndpointNeedingNoCredential_SendsNoAuthorizationHeader()
    {
        // Arrange
        HttpRequestHeaders? sentHeaders = null;
        using var handler = new FakeHttpMessageHandler((request, _) =>
        {
            sentHeaders = request.Headers;

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(OneVectorOfWidth(4), Encoding.UTF8, "application/json"),
            });
        });
        using var transport = new HttpClient(handler, disposeHandler: false);
        using var credential = ProviderEndpointCredential.Unauthenticated();

        var endpoint = EmbeddingDeclarations.Endpoint("local-server", address: "http://model-server:8000/v1");
        using var generator = this.factory.OpenEmbeddingGenerator(endpoint, credential, transport);

        // Act
        await generator.GenerateAsync(["a passage"], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(sentHeaders);
        Assert.Null(sentHeaders.Authorization);
        Assert.DoesNotContain(sentHeaders, header => header.Key.Contains("api-key", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The other role reaches the same server through the same construction, so neither is left unreachable.</summary>
    [Fact]
    public void OpenChatClient_AnEndpointNeedingNoCredential_OpensAClient()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
        using var transport = new HttpClient(handler, disposeHandler: false);
        using var credential = ProviderEndpointCredential.Unauthenticated();

        var endpoint = ChatDeclarations.Endpoint("local-server", address: "http://model-server:8000/v1");

        // Act
        using var client = this.factory.OpenChatClient(endpoint, credential, transport);

        // Assert
        Assert.NotNull(client);
    }

    /// <summary>An endpoint with no address of its own is the provider's first-party API at the library's default.</summary>
    [Fact]
    public void OpenEmbeddingGenerator_AnEndpointWithNoAddress_OpensAGeneratorAtTheProviderDefault()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
        using var transport = new HttpClient(handler, disposeHandler: false);
        using var credential = ProviderEndpointCredential.FromApiKey("a-resolved-key", resolvedMaterial: null);

        // Act
        using var generator = this.factory.OpenEmbeddingGenerator(
            EmbeddingDeclarations.Endpoint(address: null),
            credential,
            transport);

        // Assert
        Assert.NotNull(generator);
    }

    /// <summary>The other half of what this factory serves: the same construction, opened as a chat client instead.</summary>
    [Fact]
    public void OpenChatClient_AnEndpointAuthenticatedWithAKey_OpensAClient()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
        using var transport = new HttpClient(handler, disposeHandler: false);
        using var credential = ProviderEndpointCredential.FromApiKey("a-resolved-key", resolvedMaterial: null);

        // Act
        using var client = this.factory.OpenChatClient(ChatDeclarations.Endpoint(), credential, transport);

        // Assert
        Assert.NotNull(client);
    }

    /// <summary>
    /// A chat endpoint reached with a Microsoft Entra credential is a deployment with no key to provision, and it is
    /// the path no other test exercises: everything else that opens a chat client supplies a resolved key.
    /// </summary>
    [Fact]
    public void OpenChatClient_AnEndpointAuthenticatedWithAnEntraCredential_OpensAClient()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
        using var transport = new HttpClient(handler, disposeHandler: false);
        using var credential = ProviderEndpointCredential.FromEntra(
            new EntraCredentialDeclaration(
                ProviderEndpointCredentialKind.ManagedIdentity,
                "https://ai.example.invalid/.default",
                TenantId: null,
                ClientId: null,
                ClientSecret: null,
                CertificatePath: null,
                CertificatePassword: null),
            resolvedMaterial: null);

        var endpoint = ChatDeclarations.Endpoint(
            "cloud-deployment",
            address: "https://resource.cloud.invalid/openai/v1/",
            routedModelName: "a-chat-deployment");

        // Act
        using var client = this.factory.OpenChatClient(endpoint, credential, transport);

        // Assert
        Assert.NotNull(client);
    }

    /// <summary>An endpoint with no address of its own is the provider's first-party API at the library's default.</summary>
    [Fact]
    public void OpenChatClient_AnEndpointWithNoAddress_OpensAClientAtTheProviderDefault()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
        using var transport = new HttpClient(handler, disposeHandler: false);
        using var credential = ProviderEndpointCredential.FromApiKey("a-resolved-key", resolvedMaterial: null);

        // Act
        using var client = this.factory.OpenChatClient(
            ChatDeclarations.Endpoint(address: null),
            credential,
            transport);

        // Assert
        Assert.NotNull(client);
    }

    /// <summary>
    /// The responses API is the second surface one endpoint may be reached through, and it is opened over both
    /// credential shapes for the reason the chat completions one is: a cloud deployment with no key to provision must
    /// not be the shape nothing exercised.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OpenChatClient_AnEndpointDeclaringTheResponsesApi_OpensAClient(bool authenticatedWithAKey)
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
        using var transport = new HttpClient(handler, disposeHandler: false);
        using var credential = authenticatedWithAKey
            ? ProviderEndpointCredential.FromApiKey("a-resolved-key", resolvedMaterial: null)
            : ProviderEndpointCredential.FromEntra(
                new EntraCredentialDeclaration(
                    ProviderEndpointCredentialKind.ManagedIdentity,
                    "https://ai.example.invalid/.default",
                    TenantId: null,
                    ClientId: null,
                    ClientSecret: null,
                    CertificatePath: null,
                    CertificatePassword: null),
                resolvedMaterial: null);

        // Act
        using var client = this.factory.OpenChatClient(
            ChatDeclarations.Endpoint(api: ChatProviderApi.Responses),
            credential,
            transport);

        // Assert
        Assert.NotNull(client);
    }

    /// <summary>
    /// The factory takes an endpoint rather than a validated plan, so a value naming no API has to be refused here as
    /// well: opening the wrong surface would send a credential-bearing request to a path nobody declared.
    /// </summary>
    [Fact]
    public void OpenChatClient_AnEndpointNamingNoApi_IsRefused()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
        using var transport = new HttpClient(handler, disposeHandler: false);
        using var credential = ProviderEndpointCredential.FromApiKey("a-resolved-key", resolvedMaterial: null);

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => this.factory.OpenChatClient(
            ChatDeclarations.Endpoint(api: (ChatProviderApi)7),
            credential,
            transport));
    }

    /// <summary>The narrowest embeddings answer a client will read, so a request can be sent and its headers inspected.</summary>
    private static string OneVectorOfWidth(int width)
    {
        var components = Enumerable
            .Range(0, width)
            .Select(_ => (1d / Math.Sqrt(width)).ToString("R", CultureInfo.InvariantCulture));

        return "{\"object\":\"list\",\"model\":\"an-embedding-model\",\"data\":[{\"object\":\"embedding\","
            + $"\"index\":0,\"embedding\":[{string.Join(',', components)}]}}],"
            + "\"usage\":{\"prompt_tokens\":1,\"total_tokens\":1}}";
    }
}

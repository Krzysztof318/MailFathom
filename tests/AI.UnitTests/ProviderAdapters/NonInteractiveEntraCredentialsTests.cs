// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Azure.Identity;
using MailFathom.AI.Embeddings;
using MailFathom.AI.ProviderAdapters;
using Xunit;

namespace MailFathom.AI.UnitTests.ProviderAdapters;

/// <summary>Covers the credential chain a background service is allowed to hold, and the shapes it refuses.</summary>
/// <remarks>
/// Constructing a credential contacts nothing: a token is fetched at the first request, not here. What these tests
/// establish is that MailFathom composes its four shapes explicitly, so no interactive credential and no developer-tool
/// credential is reachable from a deployed service.
/// </remarks>
public sealed class NonInteractiveEntraCredentialsTests
{
    [Fact]
    public void Create_ASystemAssignedManagedIdentity_NeedsNoIdentifier()
    {
        // Act
        var credential = NonInteractiveEntraCredentials.Create(Declaration(EmbeddingEndpointCredentialKind.ManagedIdentity));

        // Assert
        Assert.IsType<ManagedIdentityCredential>(credential);
    }

    /// <summary>A resource carries at most one system-assigned identity and any number of user-assigned ones, so naming one is how it says which.</summary>
    [Fact]
    public void Create_AUserAssignedManagedIdentity_NamesTheIdentity()
    {
        // Arrange
        var declaration = Declaration(
            EmbeddingEndpointCredentialKind.ManagedIdentity,
            clientId: "8e6b3a1c-0000-4a00-9c00-1f2e3d4c5b6a");

        // Act
        var credential = NonInteractiveEntraCredentials.Create(declaration);

        // Assert
        Assert.IsType<ManagedIdentityCredential>(credential);
    }

    [Fact]
    public void Create_AWorkloadIdentity_HoldsNoSecret()
    {
        // Act
        var credential = NonInteractiveEntraCredentials.Create(
            Declaration(EmbeddingEndpointCredentialKind.WorkloadIdentity, tenantId: "a-directory", clientId: "an-application"));

        // Assert
        Assert.IsType<WorkloadIdentityCredential>(credential);
    }

    [Fact]
    public void Create_AClientSecretCredential_IsBuiltFromTheResolvedSecret()
    {
        // Arrange
        var declaration = Declaration(
            EmbeddingEndpointCredentialKind.ClientSecret,
            tenantId: "a-directory",
            clientId: "an-application",
            clientSecret: "a-resolved-secret");

        // Act
        var credential = NonInteractiveEntraCredentials.Create(declaration);

        // Assert
        Assert.IsType<ClientSecretCredential>(credential);
    }

    /// <summary>A shape missing what it needs fails where the declaration is read, naming the member rather than surfacing later as a refused token.</summary>
    [Theory]
    [InlineData(EmbeddingEndpointCredentialKind.ClientSecret, "", "an-application", "a-secret", "", nameof(EntraCredentialDeclaration.TenantId))]
    [InlineData(EmbeddingEndpointCredentialKind.ClientSecret, "a-directory", "", "a-secret", "", nameof(EntraCredentialDeclaration.ClientId))]
    [InlineData(EmbeddingEndpointCredentialKind.ClientSecret, "a-directory", "an-application", null, "", nameof(EntraCredentialDeclaration.ClientSecret))]
    [InlineData(EmbeddingEndpointCredentialKind.ClientCertificate, "a-directory", "an-application", null, "", nameof(EntraCredentialDeclaration.CertificatePath))]
    public void Create_AShapeMissingWhatItNeeds_IsRefused(
        EmbeddingEndpointCredentialKind kind,
        string tenantId,
        string clientId,
        string? clientSecret,
        string certificatePath,
        string expectedMember)
    {
        // Arrange
        var declaration = Declaration(kind, tenantId, clientId, clientSecret, certificatePath);

        // Act
        var refusal = Assert.Throws<InvalidOperationException>(
            () => NonInteractiveEntraCredentials.Create(declaration));

        // Assert
        Assert.Contains(expectedMember, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A key is presented directly, so reaching here with one would mean the two authentication shapes had been confused.</summary>
    [Fact]
    public void Create_AnApiKeyDeclaration_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NonInteractiveEntraCredentials.Create(Declaration(EmbeddingEndpointCredentialKind.ApiKey)));
    }

    private static EntraCredentialDeclaration Declaration(
        EmbeddingEndpointCredentialKind kind,
        string tenantId = "",
        string clientId = "",
        string? clientSecret = null,
        string certificatePath = "") =>
        new(
            kind,
            "https://ai.example.invalid/.default",
            tenantId.Length > 0 ? tenantId : null,
            clientId.Length > 0 ? clientId : null,
            clientSecret,
            certificatePath.Length > 0 ? certificatePath : null,
            CertificatePassword: null);
}

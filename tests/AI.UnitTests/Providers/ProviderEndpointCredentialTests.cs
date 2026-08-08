// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Providers;
using Xunit;

namespace MailFathom.AI.UnitTests.Providers;

/// <summary>Covers what one request presents to an endpoint, and how long the material behind it lives.</summary>
public sealed class ProviderEndpointCredentialTests
{
    [Fact]
    public void FromApiKey_CarriesTheKeyAndNoEntraDeclaration()
    {
        // Act
        using var credential = ProviderEndpointCredential.FromApiKey("a-resolved-key", resolvedMaterial: null);

        // Assert
        Assert.Equal(ProviderEndpointCredentialKind.ApiKey, credential.Kind);
        Assert.Equal("a-resolved-key", credential.ApiKey);
        Assert.Null(credential.Entra);
    }

    [Fact]
    public void FromApiKey_ABlankKey_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => ProviderEndpointCredential.FromApiKey("  ", resolvedMaterial: null));
    }

    /// <summary>A Microsoft Entra credential presents a token it fetches, so it carries no key of its own.</summary>
    [Fact]
    public void FromEntra_TakesItsKindFromTheDeclaration()
    {
        // Arrange
        var declaration = new EntraCredentialDeclaration(
            ProviderEndpointCredentialKind.WorkloadIdentity,
            "https://ai.example.invalid/.default",
            TenantId: "a-directory",
            ClientId: "an-application",
            ClientSecret: null,
            CertificatePath: null,
            CertificatePassword: null);

        // Act
        using var credential = ProviderEndpointCredential.FromEntra(declaration, resolvedMaterial: null);

        // Assert
        Assert.Equal(ProviderEndpointCredentialKind.WorkloadIdentity, credential.Kind);
        Assert.Null(credential.ApiKey);
        Assert.Same(declaration, credential.Entra);
    }

    /// <summary>
    /// The window in which a process dump could hold the material is bounded by one request rather than by uptime, so
    /// releasing the credential has to release what it was read from.
    /// </summary>
    [Fact]
    public void Dispose_ReleasesTheMaterialTheCredentialWasReadFrom()
    {
        // Arrange
        using var material = new RecordingDisposable();
        var credential = ProviderEndpointCredential.FromApiKey("a-resolved-key", material);

        // Act
        credential.Dispose();

        // Assert
        Assert.True(material.WasDisposed);
    }

    /// <summary>A record renders every member it holds into every log line the value reaches, and two of these are complete secrets.</summary>
    [Fact]
    public void EntraDeclarationToString_NamesTheShapeAndNoSecret()
    {
        // Arrange
        var declaration = new EntraCredentialDeclaration(
            ProviderEndpointCredentialKind.ClientSecret,
            "https://ai.example.invalid/.default",
            TenantId: "a-directory",
            ClientId: "an-application",
            ClientSecret: "the-application-secret",
            CertificatePath: null,
            CertificatePassword: "the-certificate-password");

        // Act
        var rendered = declaration.ToString();

        // Assert
        Assert.Contains(nameof(ProviderEndpointCredentialKind.ClientSecret), rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("the-application-secret", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("the-certificate-password", rendered, StringComparison.Ordinal);
    }

    private sealed class RecordingDisposable : IDisposable
    {
        public bool WasDisposed { get; private set; }

        public void Dispose() => this.WasDisposed = true;
    }
}

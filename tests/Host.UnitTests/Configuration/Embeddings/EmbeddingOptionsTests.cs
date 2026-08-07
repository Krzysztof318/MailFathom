// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.AI.Embeddings;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Host.Configuration.Embeddings;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Embeddings;

/// <summary>Covers what startup refuses about a declared embedding chain, and what it accepts.</summary>
public sealed class EmbeddingOptionsTests
{
    /// <summary>
    /// An instance that has not chosen a provider serves lexical search exactly as it did before, so requiring a
    /// declaration here would refuse to start every deployment with no use for one.
    /// </summary>
    [Fact]
    public void Validate_NoChainDeclared_IsAccepted()
    {
        // Arrange
        var settings = new EmbeddingOptions();

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Empty(errors);
        Assert.False(settings.IsConfigured);
    }

    [Fact]
    public void Validate_OneModelReachedThroughTwoEndpoints_IsAccepted()
    {
        // Arrange
        var settings = new EmbeddingOptions();
        settings.Endpoints.Add(Endpoint("first-party"));
        settings.Endpoints.Add(Endpoint("cloud-deployment", address: "https://resource.cloud.invalid/openai/v1/"));

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>The refusal names the property, because an operator told only that the chain disagrees diffs two blocks by hand.</summary>
    [Fact]
    public void Validate_AChainWhoseEndpointsDeclareDifferentGeometries_IsRefused()
    {
        // Arrange
        var settings = new EmbeddingOptions();
        settings.Endpoints.Add(Endpoint("primary"));

        var fallback = Endpoint("fallback", address: "https://second.invalid/v1/");
        fallback.Dimension = 768;
        settings.Endpoints.Add(fallback);

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.ErrorMessage!.Contains("dimension", StringComparison.Ordinal));
    }

    /// <summary>
    /// A model wider than an index covers is a performance decision the operator makes knowingly, not one the system
    /// absorbs: with trimming off it is refused, and the message carries both numbers.
    /// </summary>
    [Fact]
    public void Validate_AWidthAboveWhatAnIndexCovers_IsRefusedWhileTrimmingIsOff()
    {
        // Arrange
        var settings = new EmbeddingOptions();
        var endpoint = Endpoint("primary");
        endpoint.Dimension = IndexableVectorWidth.GreatestIndexable + 1;
        settings.Endpoints.Add(endpoint);

        // Act
        var errors = Validate(settings);

        // Assert
        var refusal = Assert.Single(errors);
        Assert.Contains(
            IndexableVectorWidth.GreatestIndexable.ToString(System.Globalization.CultureInfo.InvariantCulture),
            refusal.ErrorMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AWidthAboveWhatAnIndexCovers_IsAcceptedWhenTrimmingIsAllowed()
    {
        // Arrange
        var settings = new EmbeddingOptions { AllowTrimVectors = true };
        var endpoint = Endpoint("primary");
        endpoint.Dimension = IndexableVectorWidth.GreatestIndexable + 1;
        settings.Endpoints.Add(endpoint);

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_AWidthAboveWhatAColumnStores_IsRefusedEvenWhenTrimmingIsAllowed()
    {
        // Arrange
        var settings = new EmbeddingOptions { AllowTrimVectors = true };
        var endpoint = Endpoint("primary");
        endpoint.Dimension = IndexableVectorWidth.GreatestStorable + 1;
        settings.Endpoints.Add(endpoint);

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.ErrorMessage!.Contains("stores", StringComparison.Ordinal));
    }

    /// <summary>Exactly one credential authenticates an endpoint: both would leave which one is presented undecided, and neither leaves nothing to present.</summary>
    [Fact]
    public void Validate_AnEndpointDeclaringNoCredential_IsRefused()
    {
        // Arrange
        var settings = new EmbeddingOptions();
        var endpoint = Endpoint("primary");
        endpoint.ApiKey = null;
        settings.Endpoints.Add(endpoint);

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.ErrorMessage!.Contains("Exactly one", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AnEndpointDeclaringBothCredentials_IsRefused()
    {
        // Arrange
        var settings = new EmbeddingOptions();
        var endpoint = Endpoint("primary");
        endpoint.EntraCredential = new EmbeddingEntraCredentialOptions();
        settings.Endpoints.Add(endpoint);

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.ErrorMessage!.Contains("Exactly one", StringComparison.Ordinal));
    }

    /// <summary>The request carries a credential, so an unencrypted address would publish it to anyone on the path.</summary>
    [Theory]
    [InlineData("http://provider.invalid/v1/")]
    [InlineData("provider.invalid/v1/")]
    public void Validate_AnAddressThatIsNotAbsoluteHttps_IsRefused(string address)
    {
        // Arrange
        var settings = new EmbeddingOptions();
        settings.Endpoints.Add(Endpoint("primary", address: address));

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.ErrorMessage!.Contains("HTTPS", StringComparison.Ordinal));
    }

    /// <summary>An alias keys a credential, a resilience circuit, and every log line, so two endpoints cannot share one.</summary>
    [Fact]
    public void Validate_TwoEndpointsSharingAnAlias_IsRefused()
    {
        // Arrange
        var settings = new EmbeddingOptions();
        settings.Endpoints.Add(Endpoint("primary"));
        settings.Endpoints.Add(Endpoint("Primary", address: "https://second.invalid/v1/"));

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.ErrorMessage!.Contains("more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AnEndpointWithoutAnAlias_IsRefused()
    {
        // Arrange
        var settings = new EmbeddingOptions();
        settings.Endpoints.Add(Endpoint(string.Empty));

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.ErrorMessage!.Contains("Alias", StringComparison.Ordinal));
    }

    /// <summary>An instruction of spaces would register a second profile for a space identical to one already registered.</summary>
    [Fact]
    public void Validate_APassageInstructionOfWhitespace_IsRefused()
    {
        // Arrange
        var settings = new EmbeddingOptions();
        var endpoint = Endpoint("primary");
        endpoint.PassageInstruction = "   ";
        settings.Endpoints.Add(endpoint);

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.ErrorMessage!.Contains("PassageInstruction", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AGeometryNoVectorSpaceCouldHave_IsReportedRatherThanRaised()
    {
        // Arrange
        var settings = new EmbeddingOptions();
        var endpoint = Endpoint("primary");
        endpoint.Model = string.Empty;
        settings.Endpoints.Add(endpoint);

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.ErrorMessage!.Contains("does not describe a vector space", StringComparison.Ordinal));
    }

    /// <summary>An unbounded request holds the work behind it open for as long as an endpoint stays silent.</summary>
    [Fact]
    public void Validate_ARequestTimeoutThatIsNotPositive_IsRefused()
    {
        // Arrange
        var settings = new EmbeddingOptions { RequestTimeout = TimeSpan.Zero };
        settings.Endpoints.Add(Endpoint("primary"));

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.ErrorMessage!.Contains("RequestTimeout", StringComparison.Ordinal));
    }

    /// <summary>
    /// The declaration is built from primitives rather than handed in whole, because the options types are internal to
    /// the composition root and a theory's parameters are part of a public signature.
    /// </summary>
    [Theory]
    [InlineData(EmbeddingEndpointCredentialKind.ClientSecret, "", "an-application", false, "", "a-scope", "TenantId")]
    [InlineData(EmbeddingEndpointCredentialKind.ClientSecret, "a-directory", "", false, "", "a-scope", "ClientId")]
    [InlineData(EmbeddingEndpointCredentialKind.ClientSecret, "a-directory", "an-application", false, "", "a-scope", "references no secret")]
    [InlineData(EmbeddingEndpointCredentialKind.ClientCertificate, "a-directory", "an-application", false, "", "a-scope", "CertificatePath")]
    [InlineData(EmbeddingEndpointCredentialKind.ManagedIdentity, "", "", false, "", "  ", "TokenScope")]
    public void Validate_AnEntraCredentialMissingWhatItsShapeNeeds_IsRefused(
        EmbeddingEndpointCredentialKind kind,
        string tenantId,
        string clientId,
        bool referencesASecret,
        string certificatePath,
        string tokenScope,
        string expectedMember)
    {
        // Arrange
        var settings = new EmbeddingOptions();
        var endpoint = Endpoint("primary");
        endpoint.ApiKey = null;
        endpoint.EntraCredential = new EmbeddingEntraCredentialOptions
        {
            Kind = kind,
            TenantId = tenantId,
            ClientId = clientId,
            CertificatePath = certificatePath,
            TokenScope = tokenScope,
            ClientSecret = referencesASecret
                ? new ConfiguredSecret { Name = "application-secret", SecretReference = "env:APPLICATION_SECRET" }
                : null,
        };
        settings.Endpoints.Add(endpoint);

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.ErrorMessage!.Contains(expectedMember, StringComparison.Ordinal));
    }

    /// <summary>The two shapes that hold no secret at all are what a deployment on Azure or on Kubernetes should use.</summary>
    [Theory]
    [InlineData(EmbeddingEndpointCredentialKind.ManagedIdentity)]
    [InlineData(EmbeddingEndpointCredentialKind.WorkloadIdentity)]
    public void Validate_ACredentialShapeThatHoldsNoSecret_NeedsNothingElse(EmbeddingEndpointCredentialKind kind)
    {
        // Arrange
        var settings = new EmbeddingOptions();
        var endpoint = Endpoint("primary");
        endpoint.ApiKey = null;
        endpoint.EntraCredential = new EmbeddingEntraCredentialOptions { Kind = kind };
        settings.Endpoints.Add(endpoint);

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A key is declared as one, so naming it here would give a deployment two places to put the same thing.</summary>
    [Fact]
    public void Validate_AnEntraCredentialOfKindApiKey_IsRefused()
    {
        // Arrange
        var settings = new EmbeddingOptions();
        var endpoint = Endpoint("primary");
        endpoint.ApiKey = null;
        endpoint.EntraCredential = new EmbeddingEntraCredentialOptions
        {
            Kind = EmbeddingEndpointCredentialKind.ApiKey,
        };
        settings.Endpoints.Add(endpoint);

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.ErrorMessage!.Contains("kind ApiKey", StringComparison.Ordinal));
    }

    private static EmbeddingEndpointOptions Endpoint(
        string alias,
        string address = "https://provider.invalid/v1/") =>
        new()
        {
            Alias = alias,
            Provider = "openai",
            Model = "text-embedding-3-small",
            Dimension = 1536,
            DistanceMetric = EmbeddingDistanceMetric.Cosine,
            Address = address,
            ApiKey = new ConfiguredSecret { Name = $"{alias}-key", SecretReference = "env:PROVIDER_KEY" },
        };

    private static IReadOnlyList<ValidationResult> Validate(EmbeddingOptions settings) =>
        [.. settings.Validate(new ValidationContext(settings))];
}

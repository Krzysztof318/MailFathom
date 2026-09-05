// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.AI.Embeddings;
using MailFathom.AI.Providers;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Host.Configuration.Embeddings;
using MailFathom.Host.Configuration.Providers;
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
        endpoint.EntraCredential = new ProviderEntraCredentialOptions();
        settings.Endpoints.Add(endpoint);

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.ErrorMessage!.Contains("Exactly one", StringComparison.Ordinal));
    }

    /// <summary>Only the two schemes a request could be sent over are addresses at all.</summary>
    [Theory]
    [InlineData("provider.invalid/v1/")]
    [InlineData("ftp://provider.invalid/v1/")]
    public void Validate_AnAddressThatIsNotAbsoluteHttpOrHttps_IsRefused(string address)
    {
        // Arrange
        var settings = new EmbeddingOptions();
        settings.Endpoints.Add(Endpoint("primary", address: address));

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.ErrorMessage!.Contains("absolute HTTP or HTTPS", StringComparison.Ordinal));
    }

    /// <summary>The declared endpoint carries a credential, so an unencrypted address would publish it to anything on the path.</summary>
    [Fact]
    public void Validate_ACredentialOverAPlainAddress_IsRefused()
    {
        // Arrange
        var settings = new EmbeddingOptions();
        settings.Endpoints.Add(Endpoint("primary", address: "http://127.0.0.1:11434/v1"));

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.ErrorMessage!.Contains("plain http Address", StringComparison.Ordinal));
    }

    /// <summary>
    /// The shape of a model server the operator runs themselves, and the reason this role reaches the shared rule rather
    /// than keeping a copy: a scheme rule of its own would refuse what the other role accepts.
    /// </summary>
    [Fact]
    public void Validate_AnEndpointNeedingNoCredentialOnAPlainAddress_IsAccepted()
    {
        // Arrange
        var settings = new EmbeddingOptions();
        var endpoint = Endpoint("local-server", address: "http://model-server:8000/v1");
        endpoint.ApiKey = null;
        endpoint.Unauthenticated = true;
        settings.Endpoints.Add(endpoint);

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Needing no credential is one of the three shapes rather than a fourth thing beside them.</summary>
    [Fact]
    public void Validate_AnEndpointDeclaringBothAKeyAndNoCredential_IsRefused()
    {
        // Arrange
        var settings = new EmbeddingOptions();
        var endpoint = Endpoint("primary");
        endpoint.Unauthenticated = true;
        settings.Endpoints.Add(endpoint);

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.ErrorMessage!.Contains("more than one", StringComparison.Ordinal));
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
    [InlineData(ProviderEndpointCredentialKind.ClientSecret, "", "an-application", false, "", "a-scope", "TenantId")]
    [InlineData(ProviderEndpointCredentialKind.ClientSecret, "a-directory", "", false, "", "a-scope", "ClientId")]
    [InlineData(ProviderEndpointCredentialKind.ClientSecret, "a-directory", "an-application", false, "", "a-scope", "references no secret")]
    [InlineData(ProviderEndpointCredentialKind.ClientCertificate, "a-directory", "an-application", false, "", "a-scope", "CertificatePath")]
    [InlineData(ProviderEndpointCredentialKind.ManagedIdentity, "", "", false, "", "  ", "TokenScope")]
    public void Validate_AnEntraCredentialMissingWhatItsShapeNeeds_IsRefused(
        ProviderEndpointCredentialKind kind,
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
        endpoint.EntraCredential = new ProviderEntraCredentialOptions
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
    [InlineData(ProviderEndpointCredentialKind.ManagedIdentity)]
    [InlineData(ProviderEndpointCredentialKind.WorkloadIdentity)]
    public void Validate_ACredentialShapeThatHoldsNoSecret_NeedsNothingElse(ProviderEndpointCredentialKind kind)
    {
        // Arrange
        var settings = new EmbeddingOptions();
        var endpoint = Endpoint("primary");
        endpoint.ApiKey = null;
        endpoint.EntraCredential = new ProviderEntraCredentialOptions { Kind = kind };
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
        endpoint.EntraCredential = new ProviderEntraCredentialOptions
        {
            Kind = ProviderEndpointCredentialKind.ApiKey,
        };
        settings.Endpoints.Add(endpoint);

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.ErrorMessage!.Contains("kind ApiKey", StringComparison.Ordinal));
    }

    /// <summary>Needing no credential is declared on the endpoint, so naming it here would give a deployment two places to say it.</summary>
    [Fact]
    public void Validate_AnEntraCredentialOfKindUnauthenticated_IsRefused()
    {
        // Arrange
        var settings = new EmbeddingOptions();
        var endpoint = Endpoint("primary");
        endpoint.ApiKey = null;
        endpoint.EntraCredential = new ProviderEntraCredentialOptions
        {
            Kind = ProviderEndpointCredentialKind.Unauthenticated,
        };
        settings.Endpoints.Add(endpoint);

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.ErrorMessage!.Contains("kind Unauthenticated", StringComparison.Ordinal));
    }

    /// <summary>
    /// The backlog bound is what stands between an initial synchronization of a large mailbox and unbounded memory, so
    /// a value that is not positive is refused rather than read as "no bound".
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_MaxQueuedEmailsIsNotPositive_IsRefused(int maxQueuedEmails)
    {
        // Arrange
        var settings = new EmbeddingOptions { MaxQueuedEmails = maxQueuedEmails };

        // Act
        var errors = ValidateEveryProperty(settings);

        // Assert
        Assert.Contains(
            errors,
            error => error.MemberNames.Contains(nameof(EmbeddingOptions.MaxQueuedEmails), StringComparer.Ordinal));
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

    /// <summary>The shipped ceilings bound a message and a period, and pace no request at all.</summary>
    [Fact]
    public void Validate_NoCeilingsDeclared_AcceptsTheShippedDefaults()
    {
        // Arrange
        var settings = new EmbeddingOptions();

        // Act
        var errors = ValidateEveryProperty(settings);

        // Assert
        Assert.Empty(errors);
        Assert.Equal(EmbeddingInputBound.DefaultMaximumCharacterCount, settings.MaxCharactersPerEmail);
        Assert.Equal(EmbeddingOptions.DefaultMaxInputCharactersPerPeriod, settings.MaxInputCharactersPerPeriod);
        Assert.Equal(TimeSpan.FromDays(1), settings.SpendPeriod);
        Assert.Equal(0, settings.MaxRequestsPerMinute);
    }

    /// <summary>
    /// The ceilings are checked whether or not a chain was declared, because passages are cut for every synchronized
    /// message on an instance that has chosen no provider — so one of these left unvalidated is one already applying.
    /// </summary>
    [Fact]
    public void Validate_APerMessageOrRateCeilingThatCouldBoundNothing_IsRefusedWithNoChainDeclared()
    {
        // Arrange
        var settings = new EmbeddingOptions { MaxCharactersPerEmail = 0, MaxRequestsPerMinute = -1 };

        // Act
        var errors = ValidateEveryProperty(settings);

        // Assert
        Assert.False(settings.IsConfigured);
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(EmbeddingOptions.MaxCharactersPerEmail)));
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(EmbeddingOptions.MaxRequestsPerMinute)));
    }

    /// <summary>A negative ceiling describes no budget, and a period below a minute paces work rather than bounding it.</summary>
    [Fact]
    public void Validate_AnAggregateCeilingThatCouldBoundNothing_IsRefusedWithNoChainDeclared()
    {
        // Arrange
        var settings = new EmbeddingOptions
        {
            MaxInputCharactersPerPeriod = -1,
            SpendPeriod = TimeSpan.FromSeconds(1),
        };

        // Act
        var errors = ValidateEveryProperty(settings);

        // Assert
        Assert.False(settings.IsConfigured);
        Assert.Contains(
            errors,
            error => error.MemberNames.Contains(nameof(EmbeddingOptions.MaxInputCharactersPerPeriod)));
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(EmbeddingOptions.SpendPeriod)));
    }

    /// <summary>The per-owner ceiling is checked beside the deployment's, because either alone already applies.</summary>
    [Fact]
    public void Validate_APerOwnerCeilingThatCouldBoundNothing_IsRefusedWithNoChainDeclared()
    {
        // Arrange
        var settings = new EmbeddingOptions { MaxInputCharactersPerPeriodPerOwner = -1 };

        // Act
        var errors = ValidateEveryProperty(settings);

        // Assert
        Assert.False(settings.IsConfigured);
        Assert.Contains(
            errors,
            error => error.MemberNames.Contains(nameof(EmbeddingOptions.MaxInputCharactersPerPeriodPerOwner)));
    }

    /// <summary>A per-owner ceiling of zero declares none, which is what a deployment serving one owner wants.</summary>
    [Fact]
    public void Validate_APerOwnerCeilingOfZero_IsAccepted()
    {
        // Arrange
        var settings = new EmbeddingOptions { MaxInputCharactersPerPeriodPerOwner = 0 };

        // Act
        var errors = ValidateEveryProperty(settings);

        // Assert
        Assert.Empty(errors);
        Assert.Equal(0, settings.MaxInputCharactersPerPeriodPerOwner);
    }

    /// <summary>A ceiling of zero declares none, which the documentation states and the operator chose.</summary>
    [Fact]
    public void Validate_AnAggregateCeilingOfZero_IsAccepted()
    {
        // Arrange
        var settings = new EmbeddingOptions { MaxInputCharactersPerPeriod = 0 };

        // Act
        var errors = ValidateEveryProperty(settings);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A period longer than a month would leave embedding paused for weeks after one burst.</summary>
    [Fact]
    public void Validate_ASpendPeriodBeyondTheLongestAllowed_IsRefused()
    {
        // Arrange
        var settings = new EmbeddingOptions { SpendPeriod = TimeSpan.FromDays(60) };

        // Act
        var errors = ValidateEveryProperty(settings);

        // Assert
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(EmbeddingOptions.SpendPeriod)));
    }

    /// <summary>An instance that has not read the section describes nothing, which is what makes the operator's decision the whole of the control over an egress nothing scans.</summary>
    [Fact]
    public void ImageDescription_ADeploymentThatDeclaredNothing_DescribesNoImage()
    {
        // Arrange
        var settings = new EmbeddingOptions();

        // Act, Assert
        Assert.False(settings.ImageDescription.Enabled);
        Assert.Equal(EmbeddingImageDescriptionOptions.DefaultMaxPixels, settings.ImageDescription.MaxPixels);
    }

    private static IReadOnlyList<ValidationResult> Validate(EmbeddingOptions settings) =>
        [.. settings.Validate(new ValidationContext(settings))];

    /// <summary>Runs the attribute rules as well, which is what the options framework does with this type on start.</summary>
    private static List<ValidationResult> ValidateEveryProperty(EmbeddingOptions settings)
    {
        List<ValidationResult> errors = [];
        Validator.TryValidateObject(settings, new ValidationContext(settings), errors, validateAllProperties: true);

        return errors;
    }
}

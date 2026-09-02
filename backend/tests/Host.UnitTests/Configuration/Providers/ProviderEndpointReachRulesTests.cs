// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Providers;
using MailFathom.Host.Configuration.Providers;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Providers;

/// <summary>Covers the one rule both AI roles are judged by: where an endpoint is, and what a request to it presents.</summary>
/// <remarks>
/// The whole matrix is exercised here rather than twice over in the two options types, because the rule is one decision
/// and this is the one implementation of it. Each role's own tests then prove that it reaches this, which is what would
/// catch one of them quietly keeping a copy.
/// </remarks>
public sealed class ProviderEndpointReachRulesTests
{
    /// <summary>The declared credential shapes, named rather than passed, because the declaration type is internal to the host.</summary>
    private const string ApiKey = "api-key";
    private const string EntraCredential = "entra-credential";
    private const string NoCredential = "unauthenticated";

    /// <summary>
    /// Every combination in which no secret would cross a network somebody else can see. The plain address is accepted
    /// only in the last of them, which is the shape of a model server the operator runs themselves.
    /// </summary>
    [Theory]
    [InlineData("", ApiKey)]
    [InlineData("", EntraCredential)]
    [InlineData("", NoCredential)]
    [InlineData("https://provider.invalid/v1/", ApiKey)]
    [InlineData("https://resource.cloud.invalid/openai/v1/", EntraCredential)]
    [InlineData("https://models.invalid/v1/", NoCredential)]
    [InlineData("http://127.0.0.1:11434/v1", NoCredential)]
    [InlineData("http://model-server:8000/v1", NoCredential)]
    public void FindConfigurationErrors_AnEndpointPublishingNoCredential_ReportsNothing(string address, string credential)
    {
        // Arrange
        var declaration = Reached(address, credential);

        // Act
        var errors = FindErrors(declaration);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>
    /// The rule the scheme was always a proxy for. A credential on a plain hop is readable by anything on the path, and
    /// that is refused whichever shape the credential takes and whatever the address's host suggests about the network.
    /// </summary>
    [Theory]
    [InlineData("http://127.0.0.1:11434/v1", ApiKey)]
    [InlineData("http://model-server:8000/v1", ApiKey)]
    [InlineData("http://provider.invalid/v1/", ApiKey)]
    [InlineData("http://provider.invalid/v1/", EntraCredential)]
    public void FindConfigurationErrors_ACredentialOverAPlainAddress_IsRefused(string address, string credential)
    {
        // Arrange
        var declaration = Reached(address, credential);

        // Act
        var errors = FindErrors(declaration);

        // Assert
        Assert.Contains(errors, error => error.Contains("plain http Address", StringComparison.Ordinal));
    }

    /// <summary>Only the two schemes a request could be sent over are addresses at all; the rest name nothing this could reach.</summary>
    [Theory]
    [InlineData("provider.invalid/v1/")]
    [InlineData("/openai/v1/")]
    [InlineData("not an address")]
    [InlineData("ftp://provider.invalid/v1/")]
    public void FindConfigurationErrors_AnAddressThatIsNotAbsoluteHttpOrHttps_IsRefused(string address)
    {
        // Arrange
        var declaration = Reached(address, NoCredential);

        // Act
        var errors = FindErrors(declaration);

        // Assert
        Assert.Contains(errors, error => error.Contains("absolute HTTP or HTTPS", StringComparison.Ordinal));
    }

    /// <summary>
    /// The protection that survives the change: an endpoint saying nothing about its credential is what a forgotten key
    /// reference looks like, so needing none has to be written rather than left out.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_AnEndpointDeclaringNoCredentialShapeAtAll_IsRefused()
    {
        // Arrange
        var declaration = new Declaration { Address = "https://provider.invalid/v1/" };

        // Act
        var errors = FindErrors(declaration);

        // Assert
        Assert.Contains(errors, error => error.Contains("Exactly one of the three", StringComparison.Ordinal));
    }

    /// <summary>Two shapes leave unsaid which one a request presents, and that is as undecided as declaring none.</summary>
    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void FindConfigurationErrors_AnEndpointDeclaringMoreThanOneCredentialShape_IsRefused(
        bool declaresApiKey,
        bool declaresEntraCredential,
        bool declaresNoCredential)
    {
        // Arrange
        var declaration = new Declaration
        {
            Address = "https://provider.invalid/v1/",
            ApiKey = declaresApiKey ? new ConfiguredSecret { SecretReference = "env:PROVIDER_KEY" } : null,
            EntraCredential = declaresEntraCredential
                ? new ProviderEntraCredentialOptions { Kind = ProviderEndpointCredentialKind.ManagedIdentity }
                : null,
            Unauthenticated = declaresNoCredential,
        };

        // Act
        var errors = FindErrors(declaration);

        // Assert
        Assert.Contains(errors, error => error.Contains("more than one", StringComparison.Ordinal));
    }

    /// <summary>An operator told an endpoint is wrong has to be told which one, because a chain declares several.</summary>
    [Fact]
    public void FindConfigurationErrors_ARefusedDeclaration_NamesTheEndpointAndTheKeyToEdit()
    {
        // Arrange
        var declaration = Reached("http://provider.invalid/v1/", ApiKey);

        // Act
        var errors = ProviderEndpointReachRules
            .FindConfigurationErrors("Embedding endpoint 'primary'", declaration)
            .ToArray();

        // Assert
        Assert.Contains(errors, error => error.ErrorMessage!.StartsWith("Embedding endpoint 'primary'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.MemberNames.Contains("Address", StringComparer.Ordinal));
    }

    /// <summary>What the startup report reads, so a hop nobody encrypts is the same fact for the rule and for the warning.</summary>
    [Theory]
    [InlineData("http://127.0.0.1:11434/v1", true)]
    [InlineData("http://model-server:8000/v1", true)]
    [InlineData("https://provider.invalid/v1/", false)]
    [InlineData("", false)]
    [InlineData("not an address", false)]
    public void IsReachedInClearText_ADeclaredAddress_ReportsWhetherAnythingEncryptsTheHop(string address, bool expected)
    {
        // Arrange, Act
        var reachedInClearText = ProviderEndpointReachRules.IsReachedInClearText(address);

        // Assert
        Assert.Equal(expected, reachedInClearText);
    }

    private static Declaration Reached(string address, string credential) => new()
    {
        Address = address,
        ApiKey = credential == ApiKey ? new ConfiguredSecret { SecretReference = "env:PROVIDER_KEY" } : null,
        EntraCredential = credential == EntraCredential
            ? new ProviderEntraCredentialOptions { Kind = ProviderEndpointCredentialKind.ManagedIdentity }
            : null,
        Unauthenticated = credential == NoCredential,
    };

    private static IReadOnlyList<string> FindErrors(Declaration declaration) =>
    [
        .. ProviderEndpointReachRules
            .FindConfigurationErrors("Embedding endpoint 'primary'", declaration)
            .Select(result => result.ErrorMessage ?? string.Empty),
    ];

    /// <summary>Stands in for either options type, so the rule is exercised without either role's other settings taking part.</summary>
    private sealed class Declaration : IProviderEndpointReachDeclaration
    {
        public string Address { get; init; } = string.Empty;

        public ConfiguredSecret? ApiKey { get; init; }

        public ProviderEntraCredentialOptions? EntraCredential { get; init; }

        public bool Unauthenticated { get; init; }
    }
}

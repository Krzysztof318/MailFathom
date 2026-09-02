// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Infrastructure.SensitiveContent.Secrets;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.SensitiveContent.Secrets;

/// <summary>Covers what the secret scanner declares, which is what a configured name is judged against at startup.</summary>
public sealed class SecretContentCatalogTests
{
    private readonly SecretContentCatalog catalog = new();

    [Fact]
    public void Scanner_IsTheSecretsSwitch()
    {
        // Act, Assert
        Assert.Equal(SensitiveContentScannerKind.Secrets, this.catalog.Scanner);
    }

    /// <summary>Naming the categories in configuration replaces the defaults, so what the defaults are is a contract.</summary>
    [Fact]
    public void Categories_EveryShapeIsOnByDefaultAndOnlyTheEntropyLayerIsNot()
    {
        // Act
        var byDefault = this.catalog.Categories
            .Where(definition => definition.DetectedByDefault)
            .Select(definition => definition.Category.Name)
            .ToArray();

        // Assert
        Assert.Equal(
            ["ProviderToken", "CloudAccessKey", "PrivateKey", "JsonWebToken", "ConnectionString", "CredentialUrl"],
            byDefault);
        Assert.False(
            Assert.Single(this.catalog.Categories, definition => definition.Category.Name == "HighEntropyString")
                .DetectedByDefault);
    }

    /// <summary>A rule that runs and cannot be named is a rule nobody can switch off, so the two lists are the same list.</summary>
    [Fact]
    public void Categories_DeclareEveryRuleTheCorpusCanMatchUnder()
    {
        // Act
        var declared = this.catalog.Categories.SelectMany(definition => definition.Rules).ToHashSet();

        // Assert
        Assert.All(SecretRuleCorpus.Rules, definition => Assert.Contains(definition.Rule, declared));
        Assert.Contains(SecretRuleCorpus.UnnamedProviderCredential, declared);
    }

    /// <summary>Both corpora reach a mailbox, so the declaration has to carry both rather than only the one written here.</summary>
    [Theory]
    [InlineData("ProviderToken", "github-pat")]
    [InlineData("ProviderToken", "AzureCosmosDBIdentifiableKey")]
    [InlineData("CloudAccessKey", "aws-access-token")]
    [InlineData("PrivateKey", "private-key")]
    [InlineData("PrivateKey", "Pkcs12CertificatePrivateKeyBundle")]
    [InlineData("JsonWebToken", "UnclassifiedJwt")]
    [InlineData("ConnectionString", "database-connection-uri-credential")]
    [InlineData("CredentialUrl", "UrlCredentials")]
    [InlineData("HighEntropyString", "Unclassified32ByteBase64String")]
    public void Categories_HoldTheRuleEachCorpusContributes(string category, string rule)
    {
        // Act
        var definition = Assert.Single(this.catalog.Categories, candidate => candidate.Category.Name == category);

        // Assert
        Assert.Contains(definition.Rules, declared => declared.HasName(rule));
    }
}

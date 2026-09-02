// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using Xunit;

namespace MailFathom.Application.UnitTests.SensitiveContent;

/// <summary>Covers a rule always carrying the category it belongs to, and the grammar corpus entries are admitted under.</summary>
public sealed class SensitiveContentRuleTests
{
    private static readonly SensitiveContentCategory Category = SensitiveContentCategory.Create("CloudKey");

    /// <summary>Rule names are carried across from third-party corpora, so the grammar admits what those corpora already spell.</summary>
    [Theory]
    [InlineData("aws-access-token")]
    [InlineData("1password-secret-key")]
    [InlineData("gcp.api.key")]
    public void Create_AcceptableName_CarriesTheCategoryItBelongsTo(string name)
    {
        // Act
        var rule = SensitiveContentRule.Create(Category, name);

        // Assert
        Assert.Equal(Category, rule.Category);
        Assert.Equal(name, rule.Name);
        Assert.Equal($"CloudKey:{name}", rule.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("-leading-dash")]
    [InlineData("has space")]
    [InlineData("has\nnewline")]
    public void Create_UnacceptableName_IsRejected(string name)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => SensitiveContentRule.Create(Category, name));
    }

    [Fact]
    public void Create_WithoutACategory_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => SensitiveContentRule.Create(null!, "aws-access-token"));
    }

    [Fact]
    public void HasName_ConfiguredSpelling_MatchesIgnoringCase()
    {
        // Arrange
        var rule = SensitiveContentRule.Create(Category, "aws-access-token");

        // Act, Assert
        Assert.True(rule.HasName("AWS-Access-Token"));
        Assert.False(rule.HasName("aws-access-tokens"));
    }

    /// <summary>The same rule name under two categories is two rules, which is what stops one suppression silencing both.</summary>
    [Fact]
    public void Equality_SameNameUnderAnotherCategory_IsADifferentRule()
    {
        // Arrange
        var other = SensitiveContentCategory.Create("SigningKey");

        // Act
        var underCloudKey = SensitiveContentRule.Create(Category, "generic-api-key");
        var underSigningKey = SensitiveContentRule.Create(other, "generic-api-key");

        // Assert
        Assert.NotEqual(underCloudKey, underSigningKey);
    }
}

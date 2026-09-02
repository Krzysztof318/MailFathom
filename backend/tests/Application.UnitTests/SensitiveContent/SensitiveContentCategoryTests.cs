// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using Xunit;

namespace MailFathom.Application.UnitTests.SensitiveContent;

/// <summary>Covers the grammar a category name is held to and how a configured spelling is matched against it.</summary>
public sealed class SensitiveContentCategoryTests
{
    [Theory]
    [InlineData("AwsAccessKey")]
    [InlineData("aws-access-key")]
    [InlineData("github.token")]
    [InlineData("Personal_Name")]
    public void Create_AcceptableName_KeepsTheDeclaredSpelling(string name)
    {
        // Act
        var category = SensitiveContentCategory.Create(name);

        // Assert
        Assert.Equal(name, category.Name);
        Assert.Equal(name, category.ToString());
    }

    /// <summary>The name reaches a placeholder inside redacted text, so a bracket, a newline, or a quotation mark in one would let a rule corpus decide how the surrounding text parses.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1LeadingDigit")]
    [InlineData("has space")]
    [InlineData("has]bracket")]
    [InlineData("has\nnewline")]
    [InlineData("has\"quote")]
    public void Create_UnacceptableName_IsRejected(string name)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => SensitiveContentCategory.Create(name));
    }

    [Fact]
    public void Create_NameLongerThanTheGrammarAdmits_IsRejected()
    {
        // Arrange
        var overlyLong = new string('a', 65);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => SensitiveContentCategory.Create(overlyLong));
    }

    [Fact]
    public void Create_NullName_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => SensitiveContentCategory.Create(null!));
    }

    /// <summary>An operator writes a category by hand, so the match ignores capitalization while the declared spelling is what survives it.</summary>
    [Theory]
    [InlineData("awsaccesskey", true)]
    [InlineData("AWSACCESSKEY", true)]
    [InlineData("AwsAccessKey", true)]
    [InlineData("AwsAccessKeys", false)]
    public void HasName_ConfiguredSpelling_MatchesIgnoringCase(string configured, bool expected)
    {
        // Arrange
        var category = SensitiveContentCategory.Create("AwsAccessKey");

        // Act, Assert
        Assert.Equal(expected, category.HasName(configured));
    }

    /// <summary>Equality is ordinal, so two spellings of one category are two values and only the declared one is ever carried.</summary>
    [Fact]
    public void Equality_SameDeclaredSpelling_IsTheSameCategory()
    {
        // Act
        var declared = SensitiveContentCategory.Create("AwsAccessKey");
        var repeated = SensitiveContentCategory.Create("AwsAccessKey");
        var recapitalized = SensitiveContentCategory.Create("awsaccesskey");

        // Assert
        Assert.Equal(declared, repeated);
        Assert.NotEqual(declared, recapitalized);
    }
}

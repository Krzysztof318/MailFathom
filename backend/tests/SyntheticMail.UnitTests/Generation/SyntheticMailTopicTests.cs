// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Generation;

using Xunit;

namespace MailFathom.SyntheticMail.UnitTests.Generation;

/// <summary>The topic set the distribution draws from, and what a name means at its boundary.</summary>
public sealed class SyntheticMailTopicTests
{
    [Fact]
    public void All_EveryMemberHasADistinctName()
    {
        // Arrange, Act
        var names = SyntheticMailTopic.All.Select(topic => topic.Name).ToArray();

        // Assert
        Assert.Equal(names.Length, names.Distinct().Count());
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }

    [Fact]
    public void All_EveryMemberCarriesAPromptDescription()
    {
        // Arrange, Act
        var descriptions = SyntheticMailTopic.All.Select(topic => topic.PromptDescription);

        // Assert
        Assert.All(descriptions, description => Assert.False(string.IsNullOrWhiteSpace(description)));
    }

    [Fact]
    public void TryParse_ANameItSpellsDifferently_ResolvesToTheSameTopic()
    {
        // Arrange, Act
        Assert.True(SyntheticMailTopic.TryParse("  TECHNICAL-SUPPORT ", out var parsed));

        // Assert
        Assert.Equal(SyntheticMailTopic.TechnicalSupport, parsed);
        Assert.Equal(SyntheticMailTopic.TechnicalSupport.PromptDescription, parsed.PromptDescription);
    }

    [Theory]
    [InlineData("culinary")]
    [InlineData("business,")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_ANameNothingDeclares_IsUnspecified(string name)
    {
        // Arrange, Act
        var parsed = SyntheticMailTopic.TryParse(name, out var topic);

        // Assert
        Assert.False(parsed);
        Assert.False(topic.IsSpecified);
    }

    [Fact]
    public void TryParse_NoName_IsUnspecified()
    {
        // Arrange, Act
        var parsed = SyntheticMailTopic.TryParse(null, out var topic);

        // Assert
        Assert.False(parsed);
        Assert.False(topic.IsSpecified);
    }

    [Fact]
    public void TheDefault_IsNotATopicAndSaysSo()
    {
        // Arrange
        SyntheticMailTopic topic = default;

        // Act, Assert
        Assert.False(topic.IsSpecified);
        Assert.Equal("(unspecified)", topic.ToString());
        Assert.Throws<InvalidOperationException>(() => _ = topic.Name);
        Assert.Throws<InvalidOperationException>(() => _ = topic.PromptDescription);
    }

    [Fact]
    public void ToString_ADeclaredTopic_IsTheNameTheCommandLineAccepts()
    {
        // Arrange, Act
        var line = string.Join(", ", SyntheticMailTopic.All.Select(topic => topic.ToString()));

        // Assert
        Assert.Equal("business, invoices, technical-support, travel", line);
    }

    [Fact]
    public void Equality_ATopicIsItsOwnValue()
    {
        // Arrange, Act, Assert
        Assert.Equal(SyntheticMailTopic.Invoices, SyntheticMailTopic.Invoices);
        Assert.NotEqual(SyntheticMailTopic.Invoices, SyntheticMailTopic.Travel);
        Assert.NotEqual(SyntheticMailTopic.Invoices, default);
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Rules.Facts;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules;

/// <summary>Covers the published fact surface: its names, the shapes it declares, and how a name is read back.</summary>
public sealed class MailRuleFactTests
{
    [Fact]
    public void All_DeclaredFacts_CarryDistinctNames()
    {
        // Act
        var names = MailRuleFact.All.Select(fact => fact.Name).ToArray();

        // Assert
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>A name is written into an expression, so it has to be something the parser reads as one identifier.</summary>
    [Fact]
    public void All_DeclaredFacts_AreNamedAsPlainIdentifiers()
    {
        // Act
        var names = MailRuleFact.All.Select(fact => fact.Name).ToArray();

        // Assert
        Assert.All(
            names,
            name => Assert.True(
                char.IsAsciiLetterLower(name[0]) && name.All(char.IsAsciiLetterOrDigit),
                $"'{name}' is not a plain identifier."));
    }

    /// <summary>Only one fact costs a read of stored content, which is what the lazy resolution exists for.</summary>
    [Fact]
    public void All_FactsThatReadStoredContent_AreTheBodyTextAlone()
    {
        // Act
        var readingStoredContent = MailRuleFact.All.Where(fact => fact.ReadsStoredContent).ToArray();

        // Assert
        Assert.Equal([MailRuleFact.BodyText], readingStoredContent);
    }

    [Theory]
    [InlineData("senderDomain", MailRuleFactType.Text)]
    [InlineData("recipientDomains", MailRuleFactType.TextSet)]
    [InlineData("sizeInBytes", MailRuleFactType.Number)]
    [InlineData("isSeen", MailRuleFactType.Boolean)]
    [InlineData("receivedAt", MailRuleFactType.Timestamp)]
    public void TryParseName_DeclaredName_ResolvesTheFactWithItsShape(string name, MailRuleFactType expectedType)
    {
        // Act
        var parsed = MailRuleFact.TryParseName(name, out var fact);

        // Assert
        Assert.True(parsed);
        Assert.Equal(name, fact.Name);
        Assert.Equal(expectedType, fact.ValueType);
    }

    /// <summary>Case is significant, so the documented surface and the accepted surface cannot say different things.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("senderMailbox")]
    [InlineData("SENDERDOMAIN")]
    [InlineData("SenderDomain")]
    public void TryParseName_NameThatIsNotDeclared_ResolvesNothing(string? name)
    {
        // Act
        var parsed = MailRuleFact.TryParseName(name, out var fact);

        // Assert
        Assert.False(parsed);
        Assert.False(fact.IsSpecified);
    }

    [Fact]
    public void Name_UnspecifiedDefault_IsRefusedRatherThanAnswered()
    {
        // Arrange
        var fact = default(MailRuleFact);

        // Act, Assert
        Assert.False(fact.IsSpecified);
        Assert.Throws<InvalidOperationException>(() => fact.Name);
        Assert.Equal("(unspecified)", fact.ToString());
    }

    [Fact]
    public void ToString_DeclaredFact_IsTheNameAConditionWritesIt()
    {
        // Assert
        Assert.Equal("senderDomain", MailRuleFact.SenderDomain.ToString());
    }

    [Fact]
    public void Serialization_DeclaredFact_RoundTripsAsItsName()
    {
        // Act
        var json = JsonSerializer.Serialize(MailRuleFact.AttachmentCount);

        // Assert
        Assert.Equal("\"attachmentCount\"", json);
        Assert.Equal(MailRuleFact.AttachmentCount, JsonSerializer.Deserialize<MailRuleFact>(json));
    }

    [Fact]
    public void Serialization_AsAPropertyName_RoundTripsAsItsName()
    {
        // Arrange
        var counts = new Dictionary<MailRuleFact, int> { [MailRuleFact.Subject] = 2 };

        // Act
        var json = JsonSerializer.Serialize(counts);

        // Assert
        Assert.Equal("{\"subject\":2}", json);
        Assert.Equal(counts, JsonSerializer.Deserialize<Dictionary<MailRuleFact, int>>(json));
    }

    [Theory]
    [InlineData("\"senderMailbox\"")]
    [InlineData("7")]
    public void Deserialization_ValueThatNamesNoFact_IsRefused(string json)
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MailRuleFact>(json));
    }

    [Fact]
    public void Serialization_UnspecifiedDefault_IsRefused()
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(default(MailRuleFact)));
    }
}

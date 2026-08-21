// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;
using Xunit;

namespace MailFathom.Domain.UnitTests.Delivery;

/// <summary>
/// Covers the identity every message this system sends is threaded by: that it is minted within the sending account's
/// own domain, that no two mintings collide, and that nothing which would break the header can reach it.
/// </summary>
public sealed class InternetMessageIdTests
{
    /// <summary>The right half is the account's domain, which is what makes the identity unique without any registry.</summary>
    [Fact]
    public void Mint_SendingDomain_EndsTheIdentityWithIt()
    {
        // Act
        var messageId = InternetMessageId.Mint("example.test");

        // Assert
        Assert.EndsWith("@example.test", messageId.Value, StringComparison.Ordinal);
        Assert.NotEmpty(messageId.Value.Split('@')[0]);
    }

    /// <summary>
    /// The left half is unguessable, so two mintings never collide and nothing outside this deployment can predict the
    /// identity of a message it has not seen.
    /// </summary>
    [Fact]
    public void Mint_CalledRepeatedly_ProducesADistinctIdentityEveryTime()
    {
        // Act
        var identities = Enumerable.Range(0, 128)
            .Select(_ => InternetMessageId.Mint("example.test").Value)
            .ToArray();

        // Assert
        Assert.Equal(identities.Length, identities.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The value is written into a header, so a domain that would end it early or split it is refused.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("exam ple.test")]
    [InlineData("example.test\r\nBcc: someone@elsewhere.test")]
    [InlineData("example.test@elsewhere.test")]
    public void Mint_DomainThatCannotBeWrittenIntoAHeader_IsRefused(string domain)
    {
        // Act and assert
        Assert.Throws<ArgumentException>(() => InternetMessageId.Mint(domain));
    }

    /// <summary>Surrounding whitespace is configuration formatting rather than part of the domain.</summary>
    [Fact]
    public void Mint_DomainWithSurroundingWhitespace_UsesTheTrimmedDomain()
    {
        // Act
        var messageId = InternetMessageId.Mint("  example.test  ");

        // Assert
        Assert.EndsWith("@example.test", messageId.Value, StringComparison.Ordinal);
    }

    /// <summary>The identity is what it prints as, because it is written into a header and read out of one.</summary>
    [Fact]
    public void ToString_MintedIdentity_IsTheHeaderValueWithoutItsBrackets()
    {
        // Arrange
        var messageId = InternetMessageId.Mint("example.test");

        // Act
        var text = messageId.ToString();

        // Assert
        Assert.Equal(messageId.Value, text);
        Assert.DoesNotContain('<', text);
        Assert.DoesNotContain('>', text);
    }
}

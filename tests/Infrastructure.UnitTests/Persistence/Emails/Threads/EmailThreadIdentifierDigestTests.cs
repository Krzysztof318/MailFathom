// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Emails.Threads;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails.Threads;

/// <summary>Covers the value a message identifier is bound to its conversation under.</summary>
/// <remarks>
/// What the digest has to guarantee is that the binding key is the same width whatever a sender wrote, that the same
/// identifier reaches the same key on every pass, and that two identifiers the mail ecosystem holds apart stay apart.
/// </remarks>
public sealed class EmailThreadIdentifierDigestTests
{
    /// <summary>The column is fixed at 64 characters, so an identifier of any length has to reduce to exactly that.</summary>
    [Theory]
    [InlineData("a")]
    [InlineData("opening@example.test")]
    [InlineData("the.longest.kind.of.identifier.a.sender.might.write@a.very.long.domain.example.test")]
    public void Of_IdentifiersOfEveryLength_ProducesTheFixedWidthTheKeyIsDeclaredAt(string identifier)
    {
        // Act
        var digest = EmailThreadIdentifierDigest.Of(identifier);

        // Assert
        Assert.Equal(64, digest.Length);
        Assert.All(digest, character => Assert.True(Uri.IsHexDigit(character) && !char.IsUpper(character)));
    }

    /// <summary>A second pass over one mailbox reaches the same key, which is what makes re-derivation change nothing.</summary>
    [Fact]
    public void Of_TheSameIdentifierTwice_ProducesTheSameKey()
    {
        // Act, Assert
        Assert.Equal(
            EmailThreadIdentifierDigest.Of("opening@example.test"),
            EmailThreadIdentifierDigest.Of("opening@example.test"));
    }

    /// <summary>The mail ecosystem compares an identifier octet for octet, so nothing here folds case or trims.</summary>
    [Theory]
    [InlineData("opening@example.test", "Opening@example.test")]
    [InlineData("opening@example.test", "opening@example.test ")]
    [InlineData("opening@example.test", "reply@example.test")]
    public void Of_IdentifiersTheMailEcosystemHoldsApart_ProducesDifferentKeys(string identifier, string other)
    {
        // Act, Assert
        Assert.NotEqual(EmailThreadIdentifierDigest.Of(identifier), EmailThreadIdentifierDigest.Of(other));
    }
}

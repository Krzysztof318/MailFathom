// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;
using Xunit;

namespace MailFathom.Domain.UnitTests.Delivery;

/// <summary>Covers the value that decides whose send a caller is allowed to read back or withdraw.</summary>
/// <remarks>
/// Everything asserted here is a property the scoping rests on. Equality has to follow the identity rather than the
/// instance, difference has to follow a difference in the identity, and the width has to be fixed whatever the identity
/// was — the last one because the column it is written into is bounded and nothing above it bounds an admitted
/// identity.
/// </remarks>
public sealed class OutgoingEmailPrincipalTests
{
    [Fact]
    public void Of_TheSameIdentityTwice_ProducesEqualPrincipals()
    {
        // Arrange, Act
        var first = OutgoingEmailPrincipal.Of("issuer.example|subject-41");
        var second = OutgoingEmailPrincipal.Of("issuer.example|subject-41");

        // Assert
        Assert.Equal(first, second);
    }

    /// <summary>Two callers must never compare equal, because equality is the whole of what confines a read to one of them.</summary>
    [Fact]
    public void Of_TwoDifferentIdentities_ProducesPrincipalsThatDiffer()
    {
        // Arrange, Act
        var first = OutgoingEmailPrincipal.Of("agent-key");
        var second = OutgoingEmailPrincipal.Of("agent-key ");

        // Assert
        Assert.NotEqual(first, second);
    }

    /// <summary>An admitted identity has no bound above this, so the stored value has to have one of its own.</summary>
    [Theory]
    [InlineData("a")]
    [InlineData("anonymous")]
    [InlineData("https://login.example.test/tenants/2f6c|a-very-long-subject-identifier-nobody-bounded-anywhere-above-this-type")]
    public void Of_AnIdentityOfAnyLength_ProducesAFingerprintOfTheDeclaredWidth(string identity)
    {
        // Arrange, Act
        var principal = OutgoingEmailPrincipal.Of(identity);

        // Assert
        Assert.Equal(OutgoingEmailPrincipal.FingerprintLength, principal.Fingerprint.Length);
        Assert.All(principal.Fingerprint, character => Assert.True(char.IsAsciiHexDigitLower(character)));
    }

    /// <summary>The identity itself is not kept, and what is kept instead is one named digest of it rather than any fixed-width function of it.</summary>
    /// <remarks>
    /// Stated as the known answer rather than as an absence, because an absence is the assertion that cannot fail here:
    /// every hexadecimal string fails to contain a word, so a transformation that kept the identity in some other
    /// encoding would satisfy it. The digest below is SHA-256 over the UTF-8 bytes of the identity, written lower-case,
    /// which is what a stored fingerprint has to stay for a row written by an earlier release to go on matching the
    /// caller that queued it.
    /// </remarks>
    [Fact]
    public void Of_AnIdentity_KeepsTheDigestOfItRatherThanTheIdentity()
    {
        // Arrange, Act
        var principal = OutgoingEmailPrincipal.Of("operator@example.test");

        // Assert
        Assert.Equal("8a39ca2160d1a170b9be8e655af29232c678302815eb6f3ec98fbc7ef97f3e12", principal.Fingerprint);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Of_TextNamingNobody_IsRefused(string identity) =>
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>(() => OutgoingEmailPrincipal.Of(identity));

    [Fact]
    public void Create_TheFingerprintOfAnIdentity_RestoresAPrincipalEqualToIt()
    {
        // Arrange
        var recorded = OutgoingEmailPrincipal.Of("scheduled-agent");

        // Act
        var restored = OutgoingEmailPrincipal.Create(recorded.Fingerprint);

        // Assert
        Assert.Equal(recorded, restored);
    }

    /// <summary>A stored value that is not a fingerprint would compare unequal to every caller, hiding a send from the one entitled to it.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("not-a-fingerprint")]
    [InlineData("0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde")]
    public void Create_AValueThisSystemNeverWrote_IsRefused(string stored) =>
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>(() => OutgoingEmailPrincipal.Create(stored));
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using Xunit;

namespace MailFathom.Domain.UnitTests.Access;

/// <summary>Covers the one value every owner-facing method is resolved by: what composes it, what it refuses, and what a stored one reads back as.</summary>
public sealed class OwnerCredentialLookupTests
{
    [Fact]
    public void ForUsername_ACanonicalUsername_IsResolvedByThatFormAndNothingElse()
    {
        // Arrange
        Assert.True(OwnerCredentialUsername.TryCreate("  Ada.Lovelace@Example.Org ", out var username));

        // Act
        var lookup = OwnerCredentialLookup.ForUsername(username);

        // Assert
        Assert.True(lookup.IsSpecified);
        Assert.Equal("ada.lovelace@example.org", lookup.Value);
    }

    [Fact]
    public void ForUsername_TheUnspecifiedUsername_IsRefused()
    {
        // Act
        var composing = Record.Exception(() => OwnerCredentialLookup.ForUsername(default));

        // Assert
        Assert.IsType<ArgumentException>(composing);
    }

    [Fact]
    public void ForDigest_AValueLongerThanTheIndexHolds_IsRefused()
    {
        // Arrange
        var overlong = new string('a', OwnerCredentialLookup.MaximumLength + 1);

        // Act
        var composing = Record.Exception(() => OwnerCredentialLookup.ForDigest(overlong));

        // Assert
        Assert.IsType<ArgumentException>(composing);
    }

    /// <summary>The bound is what stops an administrative write persisting a page into the unique index, so the last value it admits is asserted beside the first it refuses.</summary>
    [Fact]
    public void TryCreate_TheLongestValueTheIndexHolds_IsAdmitted()
    {
        // Act
        var read = OwnerCredentialLookup.TryCreate(new string('a', OwnerCredentialLookup.MaximumLength), out var lookup);

        // Assert
        Assert.True(read);
        Assert.Equal(OwnerCredentialLookup.MaximumLength, lookup.Value.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("carries\u0000a control character")]
    [InlineData("carries\na newline")]
    public void TryCreate_AValueNoRowMayHold_AnswersTheUnspecifiedDefault(string? stored)
    {
        // Act
        var read = OwnerCredentialLookup.TryCreate(stored, out var lookup);

        // Assert
        Assert.False(read);
        Assert.False(lookup.IsSpecified);
    }

    [Fact]
    public void TryCreateForOAuthSubject_AnIssuerAndSubject_ComposeOneValueThatReadsBackAsBoth()
    {
        // Act
        var composed = OwnerCredentialLookup.TryCreateForOAuthSubject(
            " https://sso.example.test/realms/mailfathom ",
            " 9f2c ",
            out var lookup);

        // Assert
        Assert.True(composed);
        Assert.True(lookup.TryReadOAuthSubject(out var issuer, out var subject));
        Assert.Equal("https://sso.example.test/realms/mailfathom", issuer);
        Assert.Equal("9f2c", subject);
    }

    /// <summary>
    /// The separator is what keeps the reading unambiguous, so an issuer carrying one is refused rather than composed:
    /// admitting it would let issuer <c>https://a b</c> with subject <c>c</c> store the value issuer <c>https://a</c>
    /// with subject <c>b c</c> stores, and one authorization server's token would resolve the row provisioned for
    /// another's subject.
    /// </summary>
    [Fact]
    public void TryCreateForOAuthSubject_AnIssuerCarryingTheSeparator_IsRefusedRatherThanComposed()
    {
        // Act
        var withSpaceInTheIssuer = OwnerCredentialLookup.TryCreateForOAuthSubject("https://a b", "c", out var collided);
        var withSpaceInTheSubject = OwnerCredentialLookup.TryCreateForOAuthSubject("https://a", "b c", out var stored);

        // Assert
        Assert.False(withSpaceInTheIssuer);
        Assert.False(collided.IsSpecified);
        Assert.True(withSpaceInTheSubject);
        Assert.Equal("https://a b c", stored.Value);
    }

    [Theory]
    [InlineData(null, "9f2c")]
    [InlineData("https://sso.example.test", null)]
    [InlineData("   ", "9f2c")]
    [InlineData("https://sso.example.test", "   ")]
    public void TryCreateForOAuthSubject_AHalfThatNamesNobody_AnswersTheUnspecifiedDefault(string? issuer, string? subject)
    {
        // Act
        var composed = OwnerCredentialLookup.TryCreateForOAuthSubject(issuer, subject, out var lookup);

        // Assert
        Assert.False(composed);
        Assert.False(lookup.IsSpecified);
    }

    /// <summary>A listing renders what a credential is resolved by, and only a subject decomposes — a username or a digest is the whole value.</summary>
    [Fact]
    public void TryReadOAuthSubject_AValueThatIsNotAPair_ReportsThatItNamesNeitherHalf()
    {
        // Arrange
        var digest = OwnerCredentialLookup.ForDigest("Zm9vYmFyYmF6");

        // Act
        var read = digest.TryReadOAuthSubject(out var issuer, out var subject);

        // Assert
        Assert.False(read);
        Assert.Null(issuer);
        Assert.Null(subject);
    }

    [Fact]
    public void Default_TheUnspecifiedValue_ReportsItselfAndRefusesToAnswerForAValue()
    {
        // Arrange
        var unspecified = default(OwnerCredentialLookup);

        // Act
        var reading = Record.Exception(() => unspecified.Value);

        // Assert
        Assert.False(unspecified.IsSpecified);
        Assert.IsType<InvalidOperationException>(reading);
        Assert.False(unspecified.TryReadOAuthSubject(out _, out _));
        Assert.Equal("(unspecified)", unspecified.ToString());
    }

    /// <summary>Two credentials of one method are told apart by this value alone, so equality is the value's rather than the instance's.</summary>
    [Fact]
    public void Equality_TwoLookupsComposedFromOneValue_AreOne()
    {
        // Act
        Assert.True(OwnerCredentialLookup.TryCreate("ada@example.org", out var read));
        var composed = OwnerCredentialLookup.ForDigest("ada@example.org");

        // Assert
        Assert.Equal(composed, read);
        Assert.Equal(composed.GetHashCode(), read.GetHashCode());
    }
}

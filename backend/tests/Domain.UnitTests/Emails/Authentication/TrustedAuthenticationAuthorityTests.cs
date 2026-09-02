// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails.Authentication;
using Xunit;

namespace MailFathom.Domain.UnitTests.Emails.Authentication;

public sealed class TrustedAuthenticationAuthorityTests
{
    /// <summary>An account that configured nothing has made a choice, and the choice is to believe no header.</summary>
    [Fact]
    public void TryCreate_NoConfiguredValue_IsAcceptedAsNoAuthority()
    {
        // Act
        var created = TrustedAuthenticationAuthority.TryCreate(candidate: null, out var authority);

        // Assert
        Assert.True(created);
        Assert.False(authority.NamesAServer);
        Assert.Equal(TrustedAuthenticationAuthority.None, authority);
    }

    /// <summary>A blank or unusable value is a mistake startup has to refuse, not a way of naming no authority.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("mx example test")]
    public void TryCreate_UnusableValue_IsRefused(string candidate)
    {
        // Act
        var created = TrustedAuthenticationAuthority.TryCreate(candidate, out var authority);

        // Assert
        Assert.False(created);
        Assert.False(authority.NamesAServer);
    }

    /// <summary>A value past the length a domain name has is refused rather than truncated into a different name.</summary>
    [Fact]
    public void TryCreate_ValuePastTheDomainLengthLimit_IsRefused()
    {
        // Arrange
        var overLong = new string('a', TrustedAuthenticationAuthority.MaximumLength + 1);

        // Act
        var created = TrustedAuthenticationAuthority.TryCreate(overLong, out _);

        // Assert
        Assert.False(created);
    }

    /// <summary>A server may change the casing of its own identifier between messages, and it is still the same server.</summary>
    [Theory]
    [InlineData("mx.example.test")]
    [InlineData("MX.Example.Test")]
    [InlineData("  mx.example.test  ")]
    public void Produced_IdentifierWrittenDifferently_IsRecognized(string identifier)
    {
        // Arrange
        TrustedAuthenticationAuthority.TryCreate("MX.EXAMPLE.TEST", out var authority);

        // Act
        var produced = authority.Produced(identifier);

        // Assert
        Assert.True(produced);
    }

    /// <summary>Anything the trusted server did not write is not the trusted server's.</summary>
    [Theory]
    [InlineData("attacker.test")]
    [InlineData("")]
    [InlineData(null)]
    public void Produced_AnotherIdentifier_IsRefused(string? identifier)
    {
        // Arrange
        TrustedAuthenticationAuthority.TryCreate("mx.example.test", out var authority);

        // Act
        var produced = authority.Produced(identifier);

        // Assert
        Assert.False(produced);
    }

    /// <summary>An account naming no authority must not accidentally believe a header that wrote none either.</summary>
    [Theory]
    [InlineData("mx.example.test")]
    [InlineData("")]
    [InlineData(null)]
    public void Produced_NoAuthority_MatchesNothing(string? identifier)
    {
        // Act
        var produced = TrustedAuthenticationAuthority.None.Produced(identifier);

        // Assert
        Assert.False(produced);
    }
}

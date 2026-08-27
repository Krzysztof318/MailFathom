// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Infrastructure.Security.Passwords;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Security.Passwords;

/// <summary>Covers which header values carry a credential this deployment could judge, and which are refused before one is.</summary>
public sealed class BasicCredentialHeaderTests
{
    [Fact]
    public void TryRead_ACredentialWrittenAsRfc7617Describes_ReadsBothHalves()
    {
        // Act
        var read = BasicCredentialHeader.TryRead(Header("owner", "correcthorsebattery"), out var credential);

        // Assert
        Assert.True(read);
        Assert.NotNull(credential);

        using (credential)
        {
            Assert.Equal("owner", credential.UserId);
            Assert.True(credential.Password.SequenceEqual("correcthorsebattery"));
        }
    }

    /// <summary>HTTP matches an authentication scheme without regard to case, so a client writing it differently still authenticates.</summary>
    [Theory]
    [InlineData("Basic")]
    [InlineData("basic")]
    [InlineData("BASIC")]
    [InlineData("BaSiC")]
    public void TryRead_TheSchemeInAnyCase_ReadsTheCredential(string scheme)
    {
        // Arrange
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("owner:correcthorsebattery"));

        // Act
        var read = BasicCredentialHeader.TryRead($"{scheme} {encoded}", out var credential);

        // Assert
        Assert.True(read);
        Assert.NotNull(credential);
        credential.Dispose();
    }

    /// <summary>A password may hold as many colons as it likes; the split is the first one, so the name in front of it is what authenticates.</summary>
    [Fact]
    public void TryRead_APasswordCarryingColons_SplitsAtTheFirstOneSoTheUserIdIsUnchanged()
    {
        // Act
        var read = BasicCredentialHeader.TryRead(Header("owner", "a:b:c"), out var credential);

        // Assert
        Assert.True(read);
        Assert.NotNull(credential);

        using (credential)
        {
            Assert.Equal("owner", credential.UserId);
            Assert.True(credential.Password.SequenceEqual("a:b:c"));
        }
    }

    /// <summary>The challenge names UTF-8, so a password outside US-ASCII survives the round trip rather than folding.</summary>
    [Fact]
    public void TryRead_APasswordOutsideAscii_ReadsBackExactlyAsItWasSent()
    {
        // Arrange
        const string Password = "zażółć-gęślą-jaźń";

        // Act
        var read = BasicCredentialHeader.TryRead(Header("owner", Password), out var credential);

        // Assert
        Assert.True(read);
        Assert.NotNull(credential);

        using (credential)
        {
            Assert.True(credential.Password.SequenceEqual(Password));
        }
    }

    /// <summary>An empty password is a credential the deployment can refuse rather than a header it cannot read.</summary>
    [Fact]
    public void TryRead_ACredentialWithAnEmptyPassword_IsReadSoItIsRefusedByComparisonRatherThanBySyntax()
    {
        // Act
        var read = BasicCredentialHeader.TryRead(Header("owner", string.Empty), out var credential);

        // Assert
        Assert.True(read);
        Assert.NotNull(credential);

        using (credential)
        {
            Assert.Equal("owner", credential.UserId);
            Assert.True(credential.Password.IsEmpty);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Bearer an-opaque-api-key")]
    [InlineData("Basic")]
    [InlineData("Basic ")]
    [InlineData("Basic    ")]
    [InlineData("BasicYWJj")]
    [InlineData("Basic not base64!")]
    public void TryRead_AnythingThatIsNotOneBasicCredential_IsRefusedIdentically(string? headerValue)
    {
        // Act
        var read = BasicCredentialHeader.TryRead(headerValue, out var credential);

        // Assert
        using (credential)
        {
            Assert.False(read);
            Assert.Null(credential);
        }
    }

    /// <summary>Without a colon there are no two halves, so the value is not a credential however well it decodes.</summary>
    [Fact]
    public void TryRead_ADecodableValueCarryingNoColon_IsRefused()
    {
        // Arrange
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("ownerwithoutaseparator"));

        // Act
        var read = BasicCredentialHeader.TryRead($"Basic {encoded}", out _);

        // Assert
        Assert.False(read);
    }

    /// <summary>Strict decoding is what stops two byte sequences folding into one password through replacement characters.</summary>
    [Fact]
    public void TryRead_OctetsThatAreNotUtf8_IsRefusedRatherThanDecodedWithReplacements()
    {
        // Arrange
        var encoded = Convert.ToBase64String([(byte)'o', (byte)':', 0xC3, 0x28]);

        // Act
        var read = BasicCredentialHeader.TryRead($"Basic {encoded}", out _);

        // Assert
        Assert.False(read);
    }

    /// <summary>The bound is what stops an unauthenticated caller making this process decode a megabyte per request.</summary>
    [Fact]
    public void TryRead_AHeaderPastTheEncodedBound_IsRefusedWithoutBeingDecoded()
    {
        // Arrange
        var headerValue = $"Basic {new string('A', BasicCredentialHeader.MaximumEncodedLength)}";

        // Act
        var read = BasicCredentialHeader.TryRead(headerValue, out _);

        // Assert
        Assert.False(read);
    }

    /// <summary>The bound is set from the longest credential this deployment can hold, so nothing an operator provisions comes near it.</summary>
    [Fact]
    public void TryRead_AHeaderJustInsideTheEncodedBound_IsStillRead()
    {
        // Arrange
        var password = new string('p', 1_400);
        var headerValue = Header("owner", password);

        // Act
        var read = BasicCredentialHeader.TryRead(headerValue, out var credential);

        // Assert
        Assert.True(headerValue.Length <= BasicCredentialHeader.MaximumEncodedLength);
        Assert.True(read);
        Assert.NotNull(credential);

        using (credential)
        {
            Assert.True(credential.Password.SequenceEqual(password));
        }
    }

    /// <summary>The password lives in a buffer the credential owns, so disposing it is what ends the plaintext's life in this process.</summary>
    [Fact]
    public void Dispose_AReadCredential_ClearsThePasswordItWasHolding()
    {
        // Arrange
        BasicCredentialHeader.TryRead(Header("owner", "correcthorsebattery"), out var credential);

        // Act
        credential!.Dispose();

        // Assert
        Assert.False(credential.Password.SequenceEqual("correcthorsebattery"));
    }

    /// <summary>A value that printed one half is a value somebody eventually printed while believing it printed neither.</summary>
    [Fact]
    public void ToString_AReadCredential_ReportsNeitherHalf()
    {
        // Arrange
        BasicCredentialHeader.TryRead(Header("owner", "correcthorsebattery"), out var credential);

        // Act
        var rendered = credential!.ToString();

        // Assert
        Assert.DoesNotContain("owner", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("correcthorsebattery", rendered, StringComparison.Ordinal);
        credential.Dispose();
    }

    private static string Header(string userId, string password) =>
        $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userId}:{password}"))}";
}

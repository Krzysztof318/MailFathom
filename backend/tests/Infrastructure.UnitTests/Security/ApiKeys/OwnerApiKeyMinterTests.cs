// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Security.ApiKeys;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Security.ApiKeys;

/// <summary>Covers the one shape a minted owner key has and the digest a presented one is resolved by.</summary>
/// <remarks>
/// The deployment keeps the digest and never the key, so what these assert is that the two agree: a key this process
/// minted resolves to the value it stored for it, and a key it did not mint resolves to nothing before a row is read.
/// </remarks>
public sealed class OwnerApiKeyMinterTests
{
    private const int MintedKeyLength = 47;

    [Fact]
    public void Mint_AnyKey_CarriesThePrefixAndTheEncodedEntropyThatMakeItRecognizable()
    {
        // Arrange
        var minter = new OwnerApiKeyMinter();

        // Act
        var minted = minter.Mint();

        // Assert
        Assert.StartsWith(OwnerApiKeyMinter.KeyPrefix, minted.Key, StringComparison.Ordinal);
        Assert.Equal(MintedKeyLength, minted.Key.Length);
        Assert.True(minted.Lookup.IsSpecified);
    }

    /// <summary>The lookup a mint answers with is the one a later request resolves to, or nothing could authenticate.</summary>
    [Fact]
    public void TryDigest_TheKeyThisProcessJustMinted_ResolvesToTheLookupItWasStoredUnder()
    {
        // Arrange
        var minter = new OwnerApiKeyMinter();
        var minted = minter.Mint();

        // Act
        var recognized = minter.TryDigest(minted.Key, out var lookup);

        // Assert
        Assert.True(recognized);
        Assert.Equal(minted.Lookup, lookup);
    }

    /// <summary>Two keys are two credentials, so a mint that repeated itself would hand one owner's key to another.</summary>
    [Fact]
    public void Mint_TwoKeys_AreDifferentKeysUnderDifferentLookups()
    {
        // Arrange
        var minter = new OwnerApiKeyMinter();

        // Act
        var first = minter.Mint();
        var second = minter.Mint();

        // Assert
        Assert.NotEqual(first.Key, second.Key);
        Assert.NotEqual(first.Lookup, second.Lookup);
    }

    /// <summary>A value the shape rules already exclude is refused before anything is digested or read.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("mfk_")]
    [InlineData("mfk_tooshort")]
    [InlineData("pat_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void TryDigest_AValueThatIsNotAKeyThisProcessMints_ResolvesToNothing(string presented)
    {
        // Arrange
        var minter = new OwnerApiKeyMinter();

        // Act
        var recognized = minter.TryDigest(presented, out var lookup);

        // Assert
        Assert.False(recognized);
        Assert.False(lookup.IsSpecified);
    }

    /// <summary>A key differing in one character resolves elsewhere, which is what makes the stored digest a credential.</summary>
    [Fact]
    public void TryDigest_AKeyWithOneCharacterChanged_ResolvesToADifferentLookup()
    {
        // Arrange
        var minter = new OwnerApiKeyMinter();
        var minted = minter.Mint();
        var altered = minted.Key[..^1] + (minted.Key[^1] == 'A' ? 'B' : 'A');

        // Act
        var recognized = minter.TryDigest(altered, out var lookup);

        // Assert
        Assert.True(recognized);
        Assert.NotEqual(minted.Lookup, lookup);
    }
}

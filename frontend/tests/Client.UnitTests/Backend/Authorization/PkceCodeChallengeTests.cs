// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using MailFathom.Client.Backend.Authorization;

namespace MailFathom.Client.UnitTests.Backend.Authorization;

/// <summary>That the proof key is the pair RFC 7636 defines, and that the secret half stays out of anything printed.</summary>
public sealed class PkceCodeChallengeTests
{
    [Fact]
    public void Create_TheChallenge_IsTheS256DigestOfTheVerifier()
    {
        // Arrange, Act
        var proofKey = PkceCodeChallenge.Create();

        // Assert
        Assert.Equal(
            Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(proofKey.Verifier))),
            proofKey.Challenge);
    }

    [Fact]
    public void Create_TheVerifier_CarriesTheFullEntropyTheSpecificationAsksFor()
    {
        // Arrange, Act
        var proofKey = PkceCodeChallenge.Create();

        // Assert
        // 32 bytes of entropy encode as 43 base64url characters, which is RFC 7636's minimum length.
        Assert.Equal(43, proofKey.Verifier.Length);
    }

    [Fact]
    public void Create_TwoPairs_ShareNothing()
    {
        // Arrange, Act
        var first = PkceCodeChallenge.Create();
        var second = PkceCodeChallenge.Create();

        // Assert
        Assert.NotEqual(first.Verifier, second.Verifier);
        Assert.NotEqual(first.Challenge, second.Challenge);
    }

    [Fact]
    public void ToString_APair_DisclosesNeitherHalf()
    {
        // Arrange
        var proofKey = PkceCodeChallenge.Create();

        // Act
        var rendered = proofKey.ToString();

        // Assert
        Assert.Equal("***", rendered);
        Assert.DoesNotContain(proofKey.Verifier, rendered, StringComparison.Ordinal);
    }
}

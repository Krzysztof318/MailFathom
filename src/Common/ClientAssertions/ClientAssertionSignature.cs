// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;

namespace MailFathom.Common.ClientAssertions;

/// <summary>Which algorithm a client signs an assertion with, given the key it holds.</summary>
/// <remarks>
/// <para>
/// The client chooses nothing here. An RSA key signs with <c>RS256</c> and an elliptic-curve key signs with the digest
/// its own curve was sized for, so the algorithm follows from the key pair the operator generated rather than from a
/// setting either side could get wrong. Every value this produces is inside the asymmetric allow-list the endpoint
/// already applies to a signed credential, which is what makes minting and verifying one decision instead of two.
/// </para>
/// <para>
/// Only the algorithms a client needs to <em>produce</em> are here. The endpoint verifies a wider set, because what it
/// accepts is a policy about signatures rather than a description of this command.
/// </para>
/// </remarks>
public static class ClientAssertionSignature
{
    /// <summary>The algorithm an RSA key signs with.</summary>
    /// <remarks>PKCS#1 v1.5 over SHA-256 rather than a probabilistic padding, because it is what every JSON Web Token implementation reads and the endpoint accepts both; a client minting one has nothing to gain from the difference.</remarks>
    public const string RsaAlgorithmName = "RS256";

    /// <summary>Names the algorithm one key signs with.</summary>
    /// <param name="signingKey">The private key an assertion is signed with.</param>
    /// <returns>The JSON Web Algorithm name, or <see langword="null" /> when the key is of a kind no permitted algorithm covers.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="signingKey" /> is <see langword="null" />.</exception>
    public static string? AlgorithmFor(AsymmetricAlgorithm signingKey)
    {
        ArgumentNullException.ThrowIfNull(signingKey);

        return signingKey switch
        {
            RSA rsa when rsa.KeySize >= ClientAssertionKeyMaterial.ShortestRsaModulusInBits => RsaAlgorithmName,
            ECDsa ecdsa => EllipticCurveAlgorithmFor(ecdsa.KeySize),
            _ => null,
        };
    }

    /// <summary>Names the algorithm an elliptic-curve key of one size signs with.</summary>
    /// <param name="keySizeInBits">The curve's field size, as the key reports it.</param>
    /// <returns>The JSON Web Algorithm name, or <see langword="null" /> when no permitted algorithm is defined over a curve that size.</returns>
    /// <remarks>The three sizes are the ones RFC 7518 defines an algorithm for. P-521 reports 521 rather than 512, which is the curve's actual field size and the reason this is a lookup rather than arithmetic.</remarks>
    public static string? EllipticCurveAlgorithmFor(int keySizeInBits) => keySizeInBits switch
    {
        256 => "ES256",
        384 => "ES384",
        521 => "ES512",
        _ => null,
    };

    /// <summary>Signs the assertion's signing input with the key that mints it.</summary>
    /// <param name="signingKey">The private key.</param>
    /// <param name="signingInput">The encoded header and payload, joined by a full stop, as the compact serialization defines it.</param>
    /// <returns>The signature, in the encoding a JSON Web Signature carries for the key's algorithm.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="signingKey" /> is <see langword="null" />.</exception>
    /// <exception cref="NotSupportedException">Thrown when the key is of a kind <see cref="AlgorithmFor" /> names no algorithm for.</exception>
    /// <remarks>
    /// The elliptic-curve signature is produced as the fixed-width concatenation of its two halves rather than as a DER
    /// sequence, because that is the encoding RFC 7518 section 3.4 defines for <c>ES256</c> and its siblings — and the
    /// one difference between a signature a verifier reads and one it rejects without saying why.
    /// </remarks>
    public static byte[] Sign(AsymmetricAlgorithm signingKey, ReadOnlySpan<byte> signingInput)
    {
        ArgumentNullException.ThrowIfNull(signingKey);

        return signingKey switch
        {
            RSA rsa => rsa.SignData(signingInput, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            ECDsa ecdsa => ecdsa.SignData(
                signingInput,
                EllipticCurveDigestFor(ecdsa.KeySize),
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
            _ => throw new NotSupportedException("The key is of a kind no permitted signature algorithm covers."),
        };
    }

    private static HashAlgorithmName EllipticCurveDigestFor(int keySizeInBits) => keySizeInBits switch
    {
        256 => HashAlgorithmName.SHA256,
        384 => HashAlgorithmName.SHA384,
        521 => HashAlgorithmName.SHA512,
        _ => throw new NotSupportedException("No permitted signature algorithm is defined over a curve of that size."),
    };
}

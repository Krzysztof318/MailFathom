// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;

namespace MailFathom.Common.ClientAssertions;

/// <summary>Which algorithm a client signs an assertion with, given the key it holds.</summary>
/// <remarks>
/// <para>
/// The client chooses nothing here. An RSA key signs with <c>RS256</c> and an elliptic-curve key signs with the
/// algorithm defined over its own curve, so the algorithm follows from the key pair the operator generated rather than
/// from a setting either side could get wrong. Every value this produces is inside the asymmetric allow-list the
/// endpoint already applies to a signed credential, which is what makes minting and verifying one decision instead of
/// two.
/// </para>
/// <para>
/// A curve is recognized by its object identifier and never by the size of its field. RFC 7518 section 3.4 defines
/// <c>ES256</c>, <c>ES384</c>, and <c>ES512</c> over the three NIST curves and over nothing else, while
/// <c>secp256k1</c> and the Brainpool curves are the same sizes — so admitting a key by its length would trust a curve
/// the allow-list was written to exclude and then label its signatures with an algorithm name that does not describe
/// them.
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

    /// <summary>The curves a signature is accepted over, and what each one is signed and named as.</summary>
    /// <remarks>
    /// Identified by object identifier, which is what the key itself carries and what survives the platform naming the
    /// same curve <c>ECDSA_P256</c>, <c>nistP256</c>, or <c>prime256v1</c> depending on where it came from.
    /// </remarks>
    private static readonly PermittedCurve[] PermittedCurves =
    [
        new("1.2.840.10045.3.1.7", "ES256", HashAlgorithmName.SHA256),
        new("1.3.132.0.34", "ES384", HashAlgorithmName.SHA384),
        new("1.3.132.0.35", "ES512", HashAlgorithmName.SHA512),
    ];

    /// <summary>Names the algorithm one key signs with.</summary>
    /// <param name="signingKey">The key an assertion is signed with, or verified against.</param>
    /// <returns>The JSON Web Algorithm name, or <see langword="null" /> when the key is of a kind no permitted algorithm covers.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="signingKey" /> is <see langword="null" />.</exception>
    public static string? AlgorithmFor(AsymmetricAlgorithm signingKey)
    {
        ArgumentNullException.ThrowIfNull(signingKey);

        return signingKey switch
        {
            RSA rsa when rsa.KeySize >= ClientAssertionKeyMaterial.ShortestRsaModulusInBits => RsaAlgorithmName,
            ECDsa ecdsa => PermittedCurveOf(ecdsa)?.AlgorithmName,
            _ => null,
        };
    }

    /// <summary>Reports whether a permitted algorithm is defined over the curve a key was generated on.</summary>
    /// <param name="signingKey">The elliptic-curve key.</param>
    /// <returns><see langword="true" /> when the curve is one of the three; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="signingKey" /> is <see langword="null" />.</exception>
    public static bool IsOverAPermittedCurve(ECDsa signingKey)
    {
        ArgumentNullException.ThrowIfNull(signingKey);

        return PermittedCurveOf(signingKey) is not null;
    }

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
                DigestOf(ecdsa),
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
            _ => throw new NotSupportedException("The key is of a kind no permitted signature algorithm covers."),
        };
    }

    private static HashAlgorithmName DigestOf(ECDsa signingKey) =>
        PermittedCurveOf(signingKey)?.Digest
        ?? throw new NotSupportedException("No permitted signature algorithm is defined over that key's curve.");

    /// <summary>Finds the entry for the curve a key was generated on.</summary>
    /// <remarks>
    /// A curve given as explicit parameters rather than as a name matches nothing and is refused, because a key that
    /// declines to say which curve it is on is not one this deployment can hold to a named allow-list.
    /// </remarks>
    private static PermittedCurve? PermittedCurveOf(ECDsa signingKey)
    {
        var curve = signingKey.ExportParameters(includePrivateParameters: false).Curve;

        if (!curve.IsNamed || curve.Oid.Value is not { Length: > 0 } curveIdentifier)
        {
            return null;
        }

        return Array.Find(
            PermittedCurves,
            permitted => string.Equals(permitted.Oid, curveIdentifier, StringComparison.Ordinal));
    }

    /// <summary>One curve a signature is accepted over.</summary>
    /// <param name="Oid">The curve's object identifier, which is how a key names it.</param>
    /// <param name="AlgorithmName">The JSON Web Algorithm defined over it.</param>
    /// <param name="Digest">The digest that algorithm signs with.</param>
    /// <remarks>
    /// A reference type rather than a struct, because the lookup below reports "no permitted curve" as
    /// <see langword="null" /> and a search over a value-type sequence cannot: it answers with the type's default, which
    /// here would be an entry carrying no object identifier and a nameless algorithm — and every unpermitted curve
    /// would be admitted under it.
    /// </remarks>
    private sealed record PermittedCurve(string Oid, string AlgorithmName, HashAlgorithmName Digest);
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;

namespace MailFathom.Common.ClientAssertions;

/// <summary>Reads one half of a client's key pair out of the PEM an operator provisioned.</summary>
/// <remarks>
/// <para>
/// Both halves are read here so that the deployment and the command refuse the same material for the same stated
/// reason. The mistake this exists to catch is the one that would otherwise go unnoticed: a private key written where
/// the public half belongs imports cleanly into every cryptographic API, so a deployment configured with one would
/// start, verify signatures correctly, and hold the very credential the method exists to keep off the host.
/// </para>
/// <para>
/// The PEM label decides which half the material is, before anything is imported. That is what makes the refusal exact —
/// the material is never partially interpreted first — and it is why an encrypted private key is reported as one rather
/// than as material that failed to parse.
/// </para>
/// <para>
/// The caller owns what it receives and disposes it. Both readers return a live <see cref="AsymmetricAlgorithm" />
/// holding key state, so ownership has to transfer for the value to be usable at all.
/// </para>
/// </remarks>
public static class ClientAssertionKeyMaterial
{
    /// <summary>The shortest RSA modulus a signature is accepted from, in bits.</summary>
    /// <remarks>
    /// Below this an RSA key is no longer a credential worth having, so it is refused where the material is read rather
    /// than left to produce signatures the endpoint would then verify correctly. It is the same reasoning the permitted
    /// algorithm list applies one level up: what a deployment accepts is a decision this repository makes, not one an
    /// operator can weaken by provisioning a smaller key.
    /// </remarks>
    public const int ShortestRsaModulusInBits = 2048;

    private const string PublicKeyLabel = "PUBLIC KEY";

    private const string EncryptedPrivateKeyLabel = "ENCRYPTED PRIVATE KEY";

    private static readonly string[] PrivateKeyLabels =
    [
        "PRIVATE KEY",
        "RSA PRIVATE KEY",
        "EC PRIVATE KEY",
        EncryptedPrivateKeyLabel,
    ];

    /// <summary>Reads the public half a deployment registers for one client.</summary>
    /// <param name="material">The provisioned PEM.</param>
    /// <param name="fault">Why the material is unusable, when it is.</param>
    /// <returns>The key, which the caller owns and disposes, or <see langword="null" /> when the material carries none.</returns>
    /// <remarks>Only a <c>PUBLIC KEY</c> block is accepted, which is the subject public key info <c>openssl pkey -pubout</c> writes and the one form that states the key's algorithm rather than requiring the reader to already know it.</remarks>
    public static AsymmetricAlgorithm? ReadPublicKey(ReadOnlySpan<char> material, out ClientAssertionKeyFault fault)
    {
        if (!TryReadLabel(material, out var label))
        {
            fault = ClientAssertionKeyFault.NotPem;

            return null;
        }

        if (!label.Equals(PublicKeyLabel, StringComparison.Ordinal))
        {
            fault = NamesAPrivateKey(label) ? ClientAssertionKeyFault.WrongHalf : ClientAssertionKeyFault.NotPem;

            return null;
        }

        return Import(material, out fault);
    }

    /// <summary>Reads the private half a client signs an assertion with.</summary>
    /// <param name="material">The operator's PEM.</param>
    /// <param name="fault">Why the material is unusable, when it is.</param>
    /// <returns>The key, which the caller owns and disposes, or <see langword="null" /> when the material carries none.</returns>
    public static AsymmetricAlgorithm? ReadPrivateKey(ReadOnlySpan<char> material, out ClientAssertionKeyFault fault)
    {
        if (!TryReadLabel(material, out var label))
        {
            fault = ClientAssertionKeyFault.NotPem;

            return null;
        }

        if (label.Equals(EncryptedPrivateKeyLabel, StringComparison.Ordinal))
        {
            fault = ClientAssertionKeyFault.EncryptedPrivateKey;

            return null;
        }

        if (!NamesAPrivateKey(label))
        {
            fault = label.Equals(PublicKeyLabel, StringComparison.Ordinal)
                ? ClientAssertionKeyFault.WrongHalf
                : ClientAssertionKeyFault.NotPem;

            return null;
        }

        return Import(material, out fault);
    }

    /// <summary>Imports the material as RSA and then as elliptic curve, reporting what neither could read.</summary>
    /// <remarks>
    /// Tried in turn rather than dispatched on the key's own algorithm identifier, because both APIs already parse that
    /// identifier and refuse material that is not theirs. Deciding it here would be a second parser of the same bytes,
    /// which is the arrangement in which the two eventually disagree.
    /// </remarks>
    private static AsymmetricAlgorithm? Import(ReadOnlySpan<char> material, out ClientAssertionKeyFault fault)
    {
        var rsa = RSA.Create();

        if (TryImportFromPem(rsa, material))
        {
            if (rsa.KeySize >= ShortestRsaModulusInBits)
            {
                fault = default;

                return rsa;
            }

            rsa.Dispose();
            fault = ClientAssertionKeyFault.ModulusTooShort;

            return null;
        }

        rsa.Dispose();

        var ecdsa = ECDsa.Create();

        if (TryImportFromPem(ecdsa, material) && ClientAssertionSignature.IsOverAPermittedCurve(ecdsa))
        {
            fault = default;

            return ecdsa;
        }

        ecdsa.Dispose();
        fault = ClientAssertionKeyFault.UnsupportedAlgorithm;

        return null;
    }

    /// <summary>Imports into one algorithm, reporting material that algorithm does not recognize as its own.</summary>
    /// <remarks>
    /// The exceptions are the API's way of saying "not mine", which is an ordinary answer here rather than a failure:
    /// the caller asks each algorithm in turn precisely because the material states which one it belongs to and only the
    /// importer reads that statement.
    /// </remarks>
    private static bool TryImportFromPem(AsymmetricAlgorithm algorithm, ReadOnlySpan<char> material)
    {
        try
        {
            algorithm.ImportFromPem(material);

            return true;
        }
        catch (Exception unreadable) when (unreadable is ArgumentException or CryptographicException)
        {
            return false;
        }
    }

    private static bool NamesAPrivateKey(ReadOnlySpan<char> label)
    {
        foreach (var privateKeyLabel in PrivateKeyLabels)
        {
            if (label.Equals(privateKeyLabel, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadLabel(ReadOnlySpan<char> material, out ReadOnlySpan<char> label)
    {
        if (PemEncoding.TryFind(material, out var fields))
        {
            label = material[fields.Label];

            return true;
        }

        label = default;

        return false;
    }
}

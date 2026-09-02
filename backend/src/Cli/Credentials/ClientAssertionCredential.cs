// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Common.ClientAssertions;

namespace MailFathom.Cli.Credentials;

/// <summary>Produces the credential a key-pair profile presents, from the private key on this machine.</summary>
/// <remarks>
/// <para>
/// The command holds no credential for such a profile — it holds the location of a key the operator generated and never
/// gave anybody, and mints a fresh assertion from it on every request. That is the point of the arrangement: nothing in
/// the credential store, in a backup of it, or in the deployment's configuration is worth stealing, and rotating means
/// generating a new pair and registering its public half.
/// </para>
/// <para>
/// The key is read from its file each time rather than opened once and kept, so a rotated key is picked up by the next
/// command and no long-lived copy of the private material sits in this process.
/// </para>
/// </remarks>
internal static class ClientAssertionCredential
{
    /// <summary>Mints one assertion for the administrative endpoint.</summary>
    /// <param name="privateKeyPath">Where the operator's private key lives.</param>
    /// <param name="mintedAt">The moment the assertion is minted, from which its short expiry is measured.</param>
    /// <returns>The bearer credential to present.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="privateKeyPath" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the key file cannot be read, or holds something that is not a usable private key.</exception>
    internal static string MintFor(string privateKeyPath, DateTimeOffset mintedAt)
    {
        ArgumentNullException.ThrowIfNull(privateKeyPath);

        using var signingKey = ReadSigningKey(privateKeyPath);

        return ClientAssertionMinter.Mint(signingKey, ClientAssertion.AdminAudience, mintedAt);
    }

    /// <summary>Reads the private key, reporting what an operator has to fix rather than what the cryptographic API raised.</summary>
    private static AsymmetricAlgorithm ReadSigningKey(string privateKeyPath)
    {
        string material;

        try
        {
            material = File.ReadAllText(privateKeyPath);
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            throw new CliFailure($"The private key at {privateKeyPath} could not be read.", unreadable);
        }

        return ClientAssertionKeyMaterial.ReadPrivateKey(material, out var fault)
            ?? throw new CliFailure($"The file at {privateKeyPath} {DescribeKeyFault(fault)}");
    }

    private static string DescribeKeyFault(ClientAssertionKeyFault fault) => fault switch
    {
        ClientAssertionKeyFault.WrongHalf =>
            "holds a public key. That is the half the deployment registers; pass the private key this command signs with.",
        ClientAssertionKeyFault.EncryptedPrivateKey =>
            "holds a password-protected private key, which this command cannot open. Decrypt it with 'openssl pkey -in <key> -out <key>' and protect the file with its own permissions instead.",
        ClientAssertionKeyFault.ModulusTooShort =>
            $"holds an RSA private key shorter than {ClientAssertionKeyMaterial.ShortestRsaModulusInBits} bits, which no deployment accepts a signature from. Generate a new key pair.",
        ClientAssertionKeyFault.UnsupportedAlgorithm =>
            "holds a private key of a kind no permitted signature algorithm covers. Generate an RSA key pair, or an elliptic-curve one over P-256, P-384, or P-521.",
        _ =>
            "is not a PEM private key. Generate one with 'openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out <key>'.",
    };
}

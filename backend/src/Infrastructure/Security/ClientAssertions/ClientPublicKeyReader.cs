// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Security.Cryptography;
using MailFathom.Application.Access.Credentials;
using MailFathom.Common.ClientAssertions;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Security.ClientAssertions;

/// <summary>Reads a client's public key into the PEM a credential row stores and the fingerprint it is resolved by.</summary>
/// <remarks>
/// <para>
/// The material is accepted in the one form <see cref="ClientAssertionKeyMaterial" /> already publishes — a
/// <c>PUBLIC KEY</c> PEM block, which is what <c>openssl pkey -pubout</c> writes — so a deployment that used to
/// configure a client public key provisions exactly the file it used to reference. What changes is where it lands, not
/// what an operator has to produce.
/// </para>
/// <para>
/// The fingerprint is the base64url SHA-256 digest of the key's subject public key info, which is the encoding the PEM
/// already carries. It is computed over the DER rather than over the text, so the same key reaches the same
/// fingerprint whatever line endings, wrapping, or trailing newline the file arrived with — a fingerprint that moved
/// with the whitespace would make a client's own <c>kid</c> unreproducible.
/// </para>
/// <para>
/// Nothing here is secret and the refusal of a private key is what keeps it that way: material carrying the wrong half
/// imports cleanly into every cryptographic API, so a deployment provisioned with one would verify signatures
/// correctly while holding the credential the method exists to keep off the host.
/// </para>
/// </remarks>
internal sealed class ClientPublicKeyReader : IClientPublicKeyReader
{
    /// <inheritdoc />
    public bool TryRead(string? written, out ClientPublicKey? publicKey)
    {
        publicKey = null;

        if (string.IsNullOrWhiteSpace(written))
        {
            return false;
        }

        using var key = ClientAssertionKeyMaterial.ReadPublicKey(written, out _);

        if (key is null)
        {
            return false;
        }

        var canonicalMaterial = key.ExportSubjectPublicKeyInfoPem();

        if (canonicalMaterial.Length > OwnerCredentialEntity.MaximumMaterialLength)
        {
            return false;
        }

        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(key.ExportSubjectPublicKeyInfo(), digest);

        if (!OwnerCredentialLookup.TryCreate(Base64Url.EncodeToString(digest), out var lookup))
        {
            return false;
        }

        publicKey = new ClientPublicKey(canonicalMaterial, lookup);

        return true;
    }

    /// <inheritdoc />
    public string DescribeAcceptedForm() =>
        "A client public key is a PEM 'PUBLIC KEY' block, which is what 'openssl pkey -in key.pem -pubout' writes. "
        + $"RSA keys are accepted from {ClientAssertionKeyMaterial.ShortestRsaModulusInBits} bits upward, and elliptic "
        + "curve keys on the NIST curves. A private key is refused, because the deployment holds only the half it "
        + "verifies with.";
}

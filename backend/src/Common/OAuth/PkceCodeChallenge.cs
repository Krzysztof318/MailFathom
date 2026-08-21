// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace MailFathom.Common.OAuth;

/// <summary>One RFC 7636 proof-key pair binding an authorization request to the token request that redeems it.</summary>
/// <param name="Verifier">The high-entropy secret sent only with the token request.</param>
/// <param name="Challenge">The SHA-256 digest of the verifier, sent with the authorization request.</param>
/// <remarks>
/// PKCE is not optional here even though a confidential client could authenticate with its secret alone. The
/// authorization code travels back through a loopback address, and on a shared machine any local process can race to
/// bind that port or read the code out of a browser history; the verifier is what makes an intercepted code useless
/// without it. Google requires PKCE for installed applications, and Microsoft requires it for public clients.
/// </remarks>
public sealed record PkceCodeChallenge(string Verifier, string Challenge)
{
    /// <summary>The verifier length in bytes before encoding, which produces 43 characters — the RFC 7636 minimum, at full entropy.</summary>
    private const int VerifierEntropyByteCount = 32;

    /// <summary>Creates a pair from cryptographically secure random material.</summary>
    /// <returns>A verifier and its S256 challenge.</returns>
    public static PkceCodeChallenge Create()
    {
        var entropy = RandomNumberGenerator.GetBytes(VerifierEntropyByteCount);
        var verifier = Base64Url.EncodeToString(entropy);

        var digest = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));

        return new PkceCodeChallenge(verifier, Base64Url.EncodeToString(digest));
    }

    /// <inheritdoc />
    /// <remarks>Redacted by construction, because the verifier is the secret half of the pair.</remarks>
    public override string ToString() => "***";
}

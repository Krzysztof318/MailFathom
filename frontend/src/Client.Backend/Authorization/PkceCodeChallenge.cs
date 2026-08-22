// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace MailFathom.Client.Backend.Authorization;

/// <summary>One RFC 7636 proof-key pair binding an authorization request to the token request that redeems it.</summary>
/// <param name="Verifier">The high-entropy secret sent only with the token request.</param>
/// <param name="Challenge">The SHA-256 digest of the verifier, sent with the authorization request.</param>
/// <remarks>
/// <para>
/// This client is a public one in both heads it runs as, and could be nothing else: a desktop binary and a WebAssembly
/// bundle are both readable by whoever runs them, so a client secret written into either would be a secret handed to
/// every user of it. The proof key is what takes the place of one.
/// </para>
/// <para>
/// It also answers the specific hazard each head's redirect carries. On the desktop the code comes back through a
/// loopback address, and any other local process can race to bind the port or read the code out of a browser history;
/// in a browser it comes back through a window the page opened, on an origin other pages can navigate to. In both, an
/// intercepted code is useless without the verifier, which never leaves this process.
/// </para>
/// <para>
/// The client's own copy of what <c>backend/src/Common/OAuth/PkceCodeChallenge.cs</c> holds for the service. Nothing
/// under <c>frontend/</c> references a backend assembly, so the specification is implemented at each end rather than
/// shared across the boundary.
/// </para>
/// </remarks>
internal sealed record PkceCodeChallenge(string Verifier, string Challenge)
{
    /// <summary>The verifier length in bytes before encoding, which produces 43 characters — the RFC 7636 minimum, at full entropy.</summary>
    private const int VerifierEntropyByteCount = 32;

    /// <summary>Creates a pair from cryptographically secure random material.</summary>
    /// <returns>A verifier and its S256 challenge.</returns>
    internal static PkceCodeChallenge Create()
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

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;

namespace MailFathom.Infrastructure.Persistence.Emails.Threads;

/// <summary>Reduces a message identifier to the fixed-width value the thread binding is keyed by.</summary>
/// <remarks>
/// <para>
/// A digest is used because the identifier cannot be a key. Nothing between a sender and this system bounds a header,
/// the stored form accepts the 998 octets RFC 5322 allows a header line, and a B-tree entry that wide is one PostgreSQL
/// refuses at insert time — which would fail the arrival transaction rather than lose a value.
/// </para>
/// <para>
/// SHA-256 is chosen for its collision resistance rather than for secrecy. Nothing here is a secret and nothing verifies
/// one, so no constant-time comparison is called for; what the digest has to guarantee is that two identifiers never
/// collapse into one thread key, which is exactly the property a shorter or non-cryptographic hash would give up.
/// </para>
/// <para>
/// The identifier is compared octet for octet by the mail ecosystem, and the domain reduction that produced it already
/// preserved case on both halves. Nothing is folded, trimmed, or normalized here for that reason: the digest is of the
/// identifier exactly as it was stored.
/// </para>
/// </remarks>
internal static class EmailThreadIdentifierDigest
{
    /// <summary>Produces the digest one message identifier is bound to its thread under.</summary>
    /// <param name="identifier">The normalized message identifier, without its angle brackets.</param>
    /// <returns>The lower-case hexadecimal SHA-256 digest of the identifier's UTF-8 encoding.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="identifier" /> is <see langword="null" />.</exception>
    public static string Of(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identifier)));
    }
}

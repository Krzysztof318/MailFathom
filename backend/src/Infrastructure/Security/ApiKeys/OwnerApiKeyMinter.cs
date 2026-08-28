// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;

namespace MailFathom.Infrastructure.Security.ApiKeys;

/// <summary>Draws the keys an owner's clients present, and reduces a presented one to its stored digest.</summary>
/// <remarks>
/// <para>
/// A key is a prefix and 256 bits this process drew from the platform's cryptographic generator, written in base64url
/// so it survives a header, a shell, an environment variable, and a configuration file unescaped. The prefix is there
/// for the reader rather than for the comparison: it says what a value found in a log, a repository, or a support
/// transcript is, which is what makes a leaked one recognizable as something to revoke.
/// </para>
/// <para>
/// The stored value is the SHA-256 digest of the key's own text. A plain digest rather than the adaptive construction a
/// password is stored under, and the difference is the entropy: a password is chosen out of a space small enough to
/// search, so its record has to be expensive to try, while a key drawn here is uniformly distributed over 2^256 and a
/// digest of it discloses nothing that could be searched at any cost. What the plain digest buys is the property the
/// owner axis needs — it is deterministic, so it can be an index, so one request resolves one row rather than being
/// compared against every key the deployment holds.
/// </para>
/// <para>
/// Nothing here is keyed on a process secret, which is deliberate and is the one place this differs from the
/// configured-key comparison beside it. That one digests under a per-process key because it compares two values it
/// holds and must not leak the length of either; this one has to find a row a previous process wrote, so the digest
/// must be reproducible across restarts and across the deployment's own upgrades.
/// </para>
/// </remarks>
internal sealed class OwnerApiKeyMinter : IOwnerApiKeyMinter
{
    /// <summary>What every key this deployment mints begins with.</summary>
    /// <remarks>Short, lower-case, and ending in an underscore, which is the shape secret scanners are written against and the shape an operator recognizes in a value they were not expecting to find.</remarks>
    internal const string KeyPrefix = "mfk_";

    /// <summary>How many random bytes a key carries.</summary>
    /// <remarks>Thirty-two, which is the width of the digest that stores it; drawing more would be entropy the digest cannot carry, and drawing less would make the key the weaker half of its own record.</remarks>
    internal const int KeyEntropyInBytes = 32;

    /// <summary>How many base64url characters <see cref="KeyEntropyInBytes" /> bytes are written as.</summary>
    private const int EncodedEntropyLength = 43;

    /// <inheritdoc />
    public MintedOwnerApiKey Mint()
    {
        var entropy = RandomNumberGenerator.GetBytes(KeyEntropyInBytes);

        try
        {
            var key = KeyPrefix + Base64Url.EncodeToString(entropy);

            return TryDigestOf(key, out var lookup)
                ? new MintedOwnerApiKey(key, lookup)
                : throw new InvalidOperationException("A key this process minted was not one it recognizes.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The shape is checked before the digest is computed, which is what keeps a request presenting anything at all
    /// from costing this process a hash and an indexed read. It discloses nothing a client did not already know: what
    /// it separates is a value that could never have been minted here from one that could, and the second is refused
    /// by the lookup finding no row.
    /// </remarks>
    public bool TryDigest(ReadOnlySpan<char> presentedKey, out OwnerCredentialLookup lookup)
    {
        lookup = default;

        if (presentedKey.Length != KeyPrefix.Length + EncodedEntropyLength
            || !presentedKey.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return TryDigestOf(presentedKey, out lookup);
    }

    /// <summary>Reduces a key's text to the value a stored credential is resolved by.</summary>
    /// <remarks>
    /// The encoded copy is held in a pinned buffer and zeroed here rather than left for the collector. What this cannot
    /// control is the <see cref="string" /> a request pipeline already materialized, which is why the whole design
    /// keeps the key's life bounded to the request rather than promising an erasure nothing can deliver.
    /// </remarks>
    private static bool TryDigestOf(ReadOnlySpan<char> key, out OwnerCredentialLookup lookup)
    {
        var encodedKey = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(key), pinned: true);

        try
        {
            Encoding.UTF8.GetBytes(key, encodedKey);

            Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(encodedKey, digest);

            return OwnerCredentialLookup.TryCreate(Base64Url.EncodeToString(digest), out lookup);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encodedKey);
        }
    }
}

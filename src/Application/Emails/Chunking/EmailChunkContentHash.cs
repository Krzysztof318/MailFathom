// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace MailFathom.Application.Emails.Chunking;

/// <summary>Identifies one chunk by the text it holds together with the rules that cut it out.</summary>
/// <remarks>
/// <para>
/// The hash is what makes re-chunking cheap and what makes a vector attributable. An unchanged message cut to unchanged
/// rules yields the same hashes, so nothing downstream is re-done; a change to the rules yields different ones even
/// where the text is identical, so no vector is left hanging on boundaries it no longer describes. A hash covering only
/// the text would report the second case as unchanged, which is the failure
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// records this type to prevent.
/// </para>
/// <para>
/// The digest is a value derived from mail content and is treated as such: it is not personal data a reader can invert,
/// but it identifies one, so it is no more loggable than the passage it stands for.
/// </para>
/// </remarks>
public readonly record struct EmailChunkContentHash
{
    /// <summary>The number of characters a hexadecimal SHA-256 digest occupies.</summary>
    public const int Length = 64;

    /// <summary>Names the scheme in the digest itself, so a later encoding cannot collide with this one.</summary>
    private const string HashDomain = "mailfathom.email-chunk.v1";

    private EmailChunkContentHash(string value) => this.Value = value;

    /// <summary>Gets the digest as sixty-four lowercase hexadecimal characters.</summary>
    /// <remarks>
    /// Text rather than <c>bytea</c>, because this value is compared, grouped, and read: re-chunking decides what to
    /// write by comparing digests, and an operator asking what a stored chunk is looks at one. Raw MIME's digest is
    /// bytes for the opposite reason — nothing reads it, one writer computes it and one reader verifies it.
    /// </remarks>
    public string Value { get; }

    /// <summary>Computes the identity of one chunk from its text and the rules that produced it.</summary>
    /// <param name="rules">The boundary rules the chunk was cut to.</param>
    /// <param name="isDerivedFromLossyHtml">Whether the text the chunk came from was inferred from markup.</param>
    /// <param name="text">The chunk's own text.</param>
    /// <returns>The chunk's content hash.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rules" /> or <paramref name="text" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Every text field is length-prefixed and every number is written big-endian, so the encoding is one-to-one and the
    /// digest depends on the values alone rather than on the machine that computed it. The lossy-HTML marker is
    /// covered as well as the rules: a passage read from markup and the same passage read from a plain-text part are
    /// worth different amounts to a later ranking, so they are not one chunk under two names.
    /// </remarks>
    public static EmailChunkContentHash Compute(EmailChunkingRules rules, bool isDerivedFromLossyHtml, string text)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(text);

        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        AppendText(digest, HashDomain);
        AppendNumber(digest, rules.RuleSetVersion);
        AppendNumber(digest, rules.TargetCharacterCount);
        AppendNumber(digest, rules.MinimumCharacterCount);
        AppendNumber(digest, rules.OverlapCharacterCount);
        AppendNumber(digest, (int)rules.SourceForm);
        AppendNumber(digest, isDerivedFromLossyHtml ? 1 : 0);
        AppendNumber(digest, rules.BoundarySeparators.Count);

        foreach (var separator in rules.BoundarySeparators)
        {
            AppendText(digest, separator);
        }

        AppendText(digest, text);

        return new EmailChunkContentHash(Convert.ToHexStringLower(digest.GetHashAndReset()));
    }

    /// <summary>Reads back a digest that was written earlier.</summary>
    /// <param name="value">The sixty-four lowercase hexadecimal characters a stored chunk carries.</param>
    /// <returns>The content hash.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the value is not a lowercase hexadecimal SHA-256 digest.</exception>
    public static EmailChunkContentHash Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length != Length || !value.All(IsLowercaseHexadecimal))
        {
            throw new ArgumentException(
                $"A chunk content hash is {Length} lowercase hexadecimal characters.",
                nameof(value));
        }

        return new EmailChunkContentHash(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;

    private static bool IsLowercaseHexadecimal(char character) =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f';

    private static void AppendNumber(IncrementalHash digest, int value)
    {
        Span<byte> encoded = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(encoded, value);
        digest.AppendData(encoded);
    }

    private static void AppendText(IncrementalHash digest, string value)
    {
        var encoded = Encoding.UTF8.GetBytes(value);

        AppendNumber(digest, encoded.Length);
        digest.AppendData(encoded);
    }
}

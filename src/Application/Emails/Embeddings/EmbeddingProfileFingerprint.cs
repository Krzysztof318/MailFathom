// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace MailFathom.Application.Emails.Embeddings;

/// <summary>Identifies one vector space by everything that decides what a vector in it means.</summary>
/// <remarks>
/// <para>
/// The profile table is unique on this value, which is what makes activation idempotent: a declaration whose geometry is
/// already registered resolves to the existing profile instead of writing a second row that would be re-embedded from
/// scratch for nothing. Returning to a previous model is therefore a switch rather than a duplicate. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>.
/// </para>
/// <para>
/// The digest names no mail and no credential. It is computed over a deployment's own configured settings, so unlike a
/// chunk's content hash it identifies nothing personal and needs no handling on that account.
/// </para>
/// </remarks>
public readonly record struct EmbeddingProfileFingerprint
{
    /// <summary>The number of characters a hexadecimal SHA-256 digest occupies.</summary>
    public const int Length = 64;

    /// <summary>Names the scheme in the digest itself, so a later encoding cannot collide with this one.</summary>
    private const string HashDomain = "mailfathom.embedding-profile.v1";

    private EmbeddingProfileFingerprint(string value) => this.Value = value;

    /// <summary>Gets the digest as sixty-four lowercase hexadecimal characters.</summary>
    /// <remarks>
    /// Text rather than <c>bytea</c>, for the reason a chunk's digest is: activation compares this value against what is
    /// registered, and an operator reading a profile row reads it.
    /// </remarks>
    public string Value { get; }

    /// <summary>Computes the fingerprint of one vector space.</summary>
    /// <param name="identity">The geometry to fingerprint.</param>
    /// <returns>The fingerprint.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="identity" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Every text field is length-prefixed and every number is written big-endian, so the encoding is one-to-one and the
    /// digest depends on the values alone rather than on the machine that computed it. An absent optional value is
    /// written as a presence marker rather than skipped, which is what keeps the encoding one-to-one over a value that
    /// may be missing rather than merely short.
    /// </remarks>
    public static EmbeddingProfileFingerprint Compute(EmbeddingProfileIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var preparation = identity.InputPreparation;

        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        AppendText(digest, HashDomain);
        AppendText(digest, identity.Provider);
        AppendText(digest, identity.ModelIdentifier);
        AppendOptionalText(digest, identity.ModelVersion);
        AppendNumber(digest, identity.Dimension);
        AppendNumber(digest, (int)identity.DistanceMetric);
        AppendNumber(digest, preparation.InputCharacterLimit);
        AppendOptionalText(digest, preparation.PassageInstruction);
        AppendNumber(digest, preparation.NormalizesVector ? 1 : 0);

        return new EmbeddingProfileFingerprint(Convert.ToHexStringLower(digest.GetHashAndReset()));
    }

    /// <summary>Reads back a fingerprint that was written earlier.</summary>
    /// <param name="value">The sixty-four lowercase hexadecimal characters a registered profile carries.</param>
    /// <returns>The fingerprint.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the value is not a lowercase hexadecimal SHA-256 digest.</exception>
    public static EmbeddingProfileFingerprint Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length != Length || !value.All(IsLowercaseHexadecimal))
        {
            throw new ArgumentException(
                $"An embedding profile fingerprint is {Length} lowercase hexadecimal characters.",
                nameof(value));
        }

        return new EmbeddingProfileFingerprint(value);
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

    private static void AppendOptionalText(IncrementalHash digest, string? value)
    {
        AppendNumber(digest, value is null ? 0 : 1);

        if (value is not null)
        {
            AppendText(digest, value);
        }
    }
}

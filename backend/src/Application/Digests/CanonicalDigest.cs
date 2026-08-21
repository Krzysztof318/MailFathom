// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace MailFathom.Application.Digests;

/// <summary>Writes the one-to-one encoding every value object in this system hashes over, and reads a digest back.</summary>
/// <remarks>
/// <para>
/// Three value objects derive an identity from configuration or content, and each of them depends on the same property:
/// that two different sets of values cannot produce one byte sequence. Length-prefixing every text field and writing
/// every number big-endian is what buys it — without the prefix, a pair of fields could be re-cut into a different pair
/// writing the same bytes, and without a fixed byte order the digest would depend on the machine that computed it.
/// </para>
/// <para>
/// What stays with each value object is everything that decides what its digest <em>means</em>: its own hash domain, its
/// own field order, its own <c>IncrementalHash</c>, and its own refusal message. This holds only how a field is written,
/// so the three cannot drift apart on the encoding while remaining free to disagree on the contents.
/// </para>
/// </remarks>
internal static class CanonicalDigest
{
    /// <summary>Answers whether stored text is a digest of the expected length in the form this system writes.</summary>
    /// <param name="value">The text a stored row carries.</param>
    /// <param name="length">The number of characters the digest occupies.</param>
    /// <returns><see langword="true" /> when the value is exactly that many lowercase hexadecimal characters.</returns>
    /// <remarks>
    /// Upper case is refused rather than accepted and lowered, because a digest read back is compared against one
    /// computed here and the computation writes lower case. Accepting both forms would let one value be stored under two
    /// spellings that a unique index reads as two rows.
    /// </remarks>
    public static bool IsHexadecimalDigest(string value, int length) =>
        value.Length == length && value.All(IsLowercaseHexadecimal);

    /// <summary>Writes a number into the digest in a byte order that does not depend on the machine.</summary>
    /// <param name="digest">The digest being accumulated.</param>
    /// <param name="value">The number to write.</param>
    public static void AppendNumber(IncrementalHash digest, int value)
    {
        Span<byte> encoded = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(encoded, value);
        digest.AppendData(encoded);
    }

    /// <summary>Writes text into the digest, preceded by the length that makes the encoding one-to-one.</summary>
    /// <param name="digest">The digest being accumulated.</param>
    /// <param name="value">The text to write.</param>
    public static void AppendText(IncrementalHash digest, string value)
    {
        var encoded = Encoding.UTF8.GetBytes(value);

        AppendNumber(digest, encoded.Length);
        digest.AppendData(encoded);
    }

    private static bool IsLowercaseHexadecimal(char character) =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f';
}

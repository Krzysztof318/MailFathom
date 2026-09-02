// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;

namespace MailFathom.Common;

/// <summary>Seals a value with AES-256-GCM so that reading where it is stored does not disclose it.</summary>
/// <remarks>
/// <para>
/// The sealed form is <c>nonce ‖ tag ‖ ciphertext</c>: a fresh 96-bit nonce, the 128-bit authentication tag, and the
/// ciphertext, in that order and in one buffer. The layout is fixed rather than configurable, because a reader has to
/// be able to open a value written by an older build.
/// </para>
/// <para>
/// Every operation takes associated data, which is authenticated but not encrypted. It is what binds a sealed value to
/// where it belongs — a credential to its endpoint, a column value to its row — so a value moved somewhere else fails
/// to open instead of quietly decrypting under the wrong meaning. There is no overload without it: a caller with
/// nothing to bind is a caller who has not yet worked out what the value is for.
/// </para>
/// <para>
/// <b>This type holds no key.</b> It is handed one and forgets it, which is deliberate: where a key comes from, how
/// long it lives, and what protects it at rest differ per consumer, and folding a single answer in here would give two
/// consumers with different threat models the weaker of the two.
/// </para>
/// </remarks>
public static class AesGcmEnvelope
{
    /// <summary>The key length this envelope uses, in bytes.</summary>
    /// <remarks>AES-256, stated so a caller generating a key does not have to infer the size from the algorithm name.</remarks>
    public const int KeySizeInBytes = 32;

    private const int NonceSizeInBytes = 12;
    private const int TagSizeInBytes = 16;

    /// <summary>Generates a key of the length this envelope expects.</summary>
    /// <returns>A cryptographically random key.</returns>
    public static byte[] CreateKey() => RandomNumberGenerator.GetBytes(KeySizeInBytes);

    /// <summary>Seals a value.</summary>
    /// <param name="key">The key, of <see cref="KeySizeInBytes" /> bytes.</param>
    /// <param name="plaintext">The value to seal.</param>
    /// <param name="associatedData">What the sealed value is bound to, authenticated but not encrypted.</param>
    /// <returns>The sealed value.</returns>
    /// <exception cref="ArgumentException">Thrown when the key is not <see cref="KeySizeInBytes" /> bytes.</exception>
    /// <exception cref="CryptographicException">Thrown when the platform refuses the operation.</exception>
    public static byte[] Seal(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> associatedData)
    {
        RefuseAKeyOfTheWrongLength(key);

        var sealedValue = new byte[NonceSizeInBytes + TagSizeInBytes + plaintext.Length];
        var nonce = sealedValue.AsSpan(0, NonceSizeInBytes);
        var tag = sealedValue.AsSpan(NonceSizeInBytes, TagSizeInBytes);
        var ciphertext = sealedValue.AsSpan(NonceSizeInBytes + TagSizeInBytes);

        RandomNumberGenerator.Fill(nonce);

        using var cipher = new AesGcm(key, TagSizeInBytes);
        cipher.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        return sealedValue;
    }

    /// <summary>Opens a sealed value.</summary>
    /// <param name="key">The key it was sealed with.</param>
    /// <param name="sealedValue">The sealed value.</param>
    /// <param name="associatedData">What it was bound to when it was sealed.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentException">Thrown when the key is not <see cref="KeySizeInBytes" /> bytes.</exception>
    /// <exception cref="CryptographicException">
    /// Thrown when the value was sealed with another key, bound to other associated data, truncated, or altered. The
    /// four are one outcome on purpose: distinguishing them would tell an attacker which part of a forgery was wrong.
    /// </exception>
    public static byte[] Open(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> sealedValue,
        ReadOnlySpan<byte> associatedData)
    {
        RefuseAKeyOfTheWrongLength(key);

        if (sealedValue.Length < NonceSizeInBytes + TagSizeInBytes)
        {
            throw new CryptographicException("The sealed value is shorter than its own header.");
        }

        var plaintext = new byte[sealedValue.Length - NonceSizeInBytes - TagSizeInBytes];

        using var cipher = new AesGcm(key, TagSizeInBytes);
        cipher.Decrypt(
            sealedValue[..NonceSizeInBytes],
            sealedValue[(NonceSizeInBytes + TagSizeInBytes)..],
            sealedValue.Slice(NonceSizeInBytes, TagSizeInBytes),
            plaintext,
            associatedData);

        return plaintext;
    }

    /// <summary>Seals text into a form that can be written where only text fits.</summary>
    /// <param name="key">The key, of <see cref="KeySizeInBytes" /> bytes.</param>
    /// <param name="plaintext">The text to seal.</param>
    /// <param name="associatedData">What the sealed value is bound to.</param>
    /// <returns>The sealed value, base64-encoded.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the key is not <see cref="KeySizeInBytes" /> bytes.</exception>
    public static string SealText(ReadOnlySpan<byte> key, string plaintext, string associatedData)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(associatedData);

        return Convert.ToBase64String(
            Seal(key, Encoding.UTF8.GetBytes(plaintext), Encoding.UTF8.GetBytes(associatedData)));
    }

    /// <summary>Opens text sealed by <see cref="SealText" />.</summary>
    /// <param name="key">The key it was sealed with.</param>
    /// <param name="sealedValue">The base64-encoded sealed value.</param>
    /// <param name="associatedData">What it was bound to when it was sealed.</param>
    /// <returns>The text.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the key is not <see cref="KeySizeInBytes" /> bytes.</exception>
    /// <exception cref="FormatException">Thrown when the value is not base64.</exception>
    /// <exception cref="CryptographicException">Thrown when the value does not open, for any of the reasons <see cref="Open" /> states.</exception>
    public static string OpenText(ReadOnlySpan<byte> key, string sealedValue, string associatedData)
    {
        ArgumentNullException.ThrowIfNull(sealedValue);
        ArgumentNullException.ThrowIfNull(associatedData);

        return Encoding.UTF8.GetString(
            Open(key, Convert.FromBase64String(sealedValue), Encoding.UTF8.GetBytes(associatedData)));
    }

    private static void RefuseAKeyOfTheWrongLength(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySizeInBytes)
        {
            throw new ArgumentException(
                $"The key is {key.Length} bytes; this envelope uses AES-256, which takes {KeySizeInBytes}.",
                nameof(key));
        }
    }
}

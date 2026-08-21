// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace MailFathom.Infrastructure.Mail.Attachments;

/// <summary>The wire form of the capability an attachment download link carries, and the rules for reading one back.</summary>
/// <remarks>
/// <para>
/// One opaque value rather than a readable path with a signature beside it. Everything the deployment needs is in the
/// payload and everything it is trusted for is in the tag over that payload, so verification is one comparison and there
/// is no second place a caller could vary something the signature does not cover.
/// </para>
/// <para>
/// The nonce is what makes two capabilities for the same attachment different values. Without it a link would be a pure
/// function of what it names and when it expires, so a holder could recognize that two links point at the same file and
/// a link reissued within the same second would be byte-identical to the previous one.
/// </para>
/// <para>
/// The key identifier travels in the payload because the ring rotates. A capability minted under the previous active key
/// stays verifiable for the rest of its own lifetime, which is what keeps a rotation from invalidating every link a
/// deployment has just handed out. It is read from untrusted input before anything is verified, so it is bounded and
/// checked before it is used to look anything up.
/// </para>
/// <para>
/// Nothing here is a secret except the signing key it is handed, and the composed value is never logged: it is an
/// unauthenticated way to obtain mail content, which makes it more sensitive than the file name it points at.
/// </para>
/// </remarks>
internal static class AttachmentDownloadCapability
{
    /// <summary>How many bytes of cryptographically secure randomness every capability carries.</summary>
    /// <remarks>128 bits, which is what makes two capabilities for one attachment unrelated values rather than a bound on forgery — the tag is what bounds forgery.</remarks>
    internal const int NonceSizeInBytes = 16;

    /// <summary>How many bytes the derived signing key holds.</summary>
    internal const int SigningKeySizeInBytes = 32;

    /// <summary>The greatest number of characters a presented capability may carry before anything parses it.</summary>
    /// <remarks>
    /// The longest capability this system mints is a little over 130 characters, so nothing it issued is refused by
    /// this. What it stops is work proportional to a request: the decode scans whatever it is handed, and the caller
    /// presenting a capability is by definition one nobody vouches for.
    /// </remarks>
    internal const int MaxLength = 512;

    /// <summary>The format marker, so a later layout is a different value rather than a differently interpreted one.</summary>
    private const byte FormatVersion = 1;

    /// <summary>The greatest number of bytes a key identifier takes, which is the grammar the configuration enforces.</summary>
    private const int MaxKeyIdLength = 64;

    private const int VersionOffset = 0;
    private const int KeyIdLengthOffset = 1;
    private const int KeyIdOffset = 2;
    private const int StoredEmailIdSizeInBytes = 16;
    private const int AttachmentPositionSizeInBytes = sizeof(int);
    private const int ExpirySizeInBytes = sizeof(long);
    private const int FixedSizeAfterKeyId =
        StoredEmailIdSizeInBytes + AttachmentPositionSizeInBytes + ExpirySizeInBytes + NonceSizeInBytes;

    /// <summary>The separator between the payload and the tag over it.</summary>
    private const char PayloadTagSeparator = '.';

    /// <summary>Composes the capability for one attachment of one email.</summary>
    /// <param name="keyId">The identifier of the ring key the signing key was derived from.</param>
    /// <param name="storedEmailId">The email the attachment belongs to.</param>
    /// <param name="attachmentPosition">The attachment's zero-based position in the walk order.</param>
    /// <param name="expiresAt">When the capability stops being redeemable.</param>
    /// <param name="signingKey">The derived signing key.</param>
    /// <returns>The composed capability, ready to be placed in a URL path.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="keyId" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="keyId" /> is empty or longer than the configuration grammar permits, or when <paramref name="attachmentPosition" /> is negative.</exception>
    internal static string Compose(
        string keyId,
        Guid storedEmailId,
        int attachmentPosition,
        DateTimeOffset expiresAt,
        ReadOnlySpan<byte> signingKey)
    {
        ArgumentNullException.ThrowIfNull(keyId);
        ArgumentOutOfRangeException.ThrowIfNegative(attachmentPosition);

        var keyIdLength = Encoding.UTF8.GetByteCount(keyId);
        ArgumentOutOfRangeException.ThrowIfZero(keyIdLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(keyIdLength, MaxKeyIdLength);

        Span<byte> payload = stackalloc byte[KeyIdOffset + MaxKeyIdLength + FixedSizeAfterKeyId];
        payload = payload[..(KeyIdOffset + keyIdLength + FixedSizeAfterKeyId)];

        payload[VersionOffset] = FormatVersion;
        payload[KeyIdLengthOffset] = (byte)keyIdLength;
        Encoding.UTF8.GetBytes(keyId, payload.Slice(KeyIdOffset, keyIdLength));

        var body = payload[(KeyIdOffset + keyIdLength)..];
        storedEmailId.TryWriteBytes(body[..StoredEmailIdSizeInBytes], bigEndian: true, out _);
        BinaryPrimitives.WriteInt32BigEndian(
            body.Slice(StoredEmailIdSizeInBytes, AttachmentPositionSizeInBytes),
            attachmentPosition);
        BinaryPrimitives.WriteInt64BigEndian(
            body.Slice(StoredEmailIdSizeInBytes + AttachmentPositionSizeInBytes, ExpirySizeInBytes),
            expiresAt.ToUnixTimeSeconds());
        RandomNumberGenerator.Fill(body[^NonceSizeInBytes..]);

        Span<byte> tag = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(signingKey, payload, tag);

        return string.Concat(
            Base64Url.EncodeToString(payload),
            PayloadTagSeparator.ToString(),
            Base64Url.EncodeToString(tag));
    }

    /// <summary>Reads the ring key a presented capability names, without trusting anything else about it.</summary>
    /// <param name="capability">The presented text, which is entirely untrusted.</param>
    /// <param name="keyId">The named key identifier when the capability is well formed; otherwise <see langword="null" />.</param>
    /// <returns><see langword="true" /> when a key identifier could be read; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// This runs before any verification, because the key a capability was signed with is what has to be resolved in
    /// order to verify it. Nothing read here is acted on beyond looking a configured key up: an identifier that names
    /// no configured key ends the redemption, and one that does still proves nothing until the tag verifies.
    /// </remarks>
    internal static bool TryReadKeyId(string? capability, out string? keyId)
    {
        keyId = null;

        if (!TryDecodePayload(capability, out var payload))
        {
            return false;
        }

        keyId = Encoding.UTF8.GetString(payload.AsSpan(KeyIdOffset, payload[KeyIdLengthOffset]));

        return true;
    }

    /// <summary>Verifies a presented capability and reads what it authorizes.</summary>
    /// <param name="capability">The presented text, which is entirely untrusted.</param>
    /// <param name="signingKey">The signing key derived from the ring key the capability named.</param>
    /// <param name="readAt">The instant the expiry is judged against, which comes from the injected time provider.</param>
    /// <param name="storedEmailId">The authorized email when the capability verifies; otherwise the empty value.</param>
    /// <param name="attachmentPosition">The authorized attachment position when the capability verifies; otherwise zero.</param>
    /// <returns><see langword="true" /> when the capability verifies and has not expired; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// The tag is compared in fixed time and before the expiry is looked at, so nothing about a forgery is decided by
    /// how much of it was right. Every refusal is one answer for the reason the port above states.
    /// </remarks>
    internal static bool TryVerify(
        string? capability,
        ReadOnlySpan<byte> signingKey,
        DateTimeOffset readAt,
        out Guid storedEmailId,
        out int attachmentPosition)
    {
        storedEmailId = Guid.Empty;
        attachmentPosition = 0;

        if (!TryDecodePayload(capability, out var payload)
            || !TryDecodeTag(capability, out var presentedTag))
        {
            return false;
        }

        Span<byte> expectedTag = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(signingKey, payload, expectedTag);

        if (!CryptographicOperations.FixedTimeEquals(expectedTag, presentedTag))
        {
            return false;
        }

        var body = payload.AsSpan(KeyIdOffset + payload[KeyIdLengthOffset]);
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(
            BinaryPrimitives.ReadInt64BigEndian(
                body.Slice(StoredEmailIdSizeInBytes + AttachmentPositionSizeInBytes, ExpirySizeInBytes)));

        if (readAt >= expiresAt)
        {
            return false;
        }

        storedEmailId = new Guid(body[..StoredEmailIdSizeInBytes], bigEndian: true);
        attachmentPosition = BinaryPrimitives.ReadInt32BigEndian(
            body.Slice(StoredEmailIdSizeInBytes, AttachmentPositionSizeInBytes));

        return attachmentPosition >= 0;
    }

    /// <summary>Decodes the payload half and checks that its shape is one this format describes.</summary>
    /// <remarks>
    /// Every structural rule is checked here rather than at the point each field is read, so no later step can index
    /// into a buffer on the strength of a length the caller chose. The length is bounded before the decode for the same
    /// reason the tool bounds an identifier before parsing it.
    /// </remarks>
    private static bool TryDecodePayload(string? capability, out byte[] payload)
    {
        payload = [];

        if (capability is not { Length: > 0 and <= MaxLength })
        {
            return false;
        }

        var separator = capability.IndexOf(PayloadTagSeparator, StringComparison.Ordinal);
        if (separator <= 0)
        {
            return false;
        }

        if (!TryDecode(capability.AsSpan(0, separator), out var decoded))
        {
            return false;
        }

        if (decoded.Length < KeyIdOffset + FixedSizeAfterKeyId || decoded[VersionOffset] != FormatVersion)
        {
            return false;
        }

        int keyIdLength = decoded[KeyIdLengthOffset];
        if (keyIdLength is 0 or > MaxKeyIdLength
            || decoded.Length != KeyIdOffset + keyIdLength + FixedSizeAfterKeyId)
        {
            return false;
        }

        payload = decoded;

        return true;
    }

    /// <summary>Decodes the tag half, whose length is fixed by the algorithm rather than by anything presented.</summary>
    private static bool TryDecodeTag(string? capability, out byte[] tag)
    {
        tag = [];

        var separator = capability!.IndexOf(PayloadTagSeparator, StringComparison.Ordinal);

        return TryDecode(capability.AsSpan(separator + 1), out tag)
            && tag.Length == HMACSHA256.HashSizeInBytes;
    }

    private static bool TryDecode(ReadOnlySpan<char> encoded, out byte[] decoded)
    {
        decoded = [];

        var buffer = new byte[Base64Url.GetMaxDecodedLength(encoded.Length)];
        if (!Base64Url.TryDecodeFromChars(encoded, buffer, out var decodedLength))
        {
            return false;
        }

        decoded = buffer[..decodedLength];

        return true;
    }
}

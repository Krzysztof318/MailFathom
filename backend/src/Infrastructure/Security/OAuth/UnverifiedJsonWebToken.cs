// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace MailFathom.Infrastructure.Security.OAuth;

/// <summary>Reads what a JSON Web Token says about itself, before anything has been verified.</summary>
/// <remarks>
/// <para>
/// A deployment can trust several authorization servers and can accept a credential a client minted for itself, and the
/// token is the only thing that says which of them it is. Choosing a validator therefore has to read the token's own
/// <c>iss</c> claim and its own declared type first — both unsigned input, chosen by whoever sent the request, and the
/// name of this type says so at every call site.
/// </para>
/// <para>
/// What the value is allowed to decide is the point. It selects which configured profile validates the token, and that
/// profile then checks the signature against its own key set and compares <c>iss</c> against its own configured issuer.
/// A token claiming an issuer nobody configured selects no profile and is refused; a token claiming one profile's issuer
/// while carrying another's signature fails that profile's signature check. So the worst an attacker achieves by writing
/// whatever they like here is to pick which validator rejects them.
/// </para>
/// <para>
/// Nothing else is read. The remaining claims are the validated token's business, and reading one here would make an
/// unverified assertion look like an established fact somewhere further down.
/// </para>
/// </remarks>
public static class UnverifiedJsonWebToken
{
    /// <summary>The largest encoded segment this reads, beyond which the token selects no profile and is refused.</summary>
    /// <remarks>An access token's payload is a few hundred bytes and its header far less. The limit stops an unauthenticated request from making the host decode and parse an arbitrarily large document before anything has been verified.</remarks>
    private const int SegmentSizeLimitInBytes = 8 * 1024;

    private const string IssuerClaimName = "iss";

    private const string TypeHeaderName = "typ";

    private const string KeyIdHeaderName = "kid";

    /// <summary>Reads the issuer a compact-serialized JSON Web Token claims.</summary>
    /// <param name="credential">The bearer credential a request presented.</param>
    /// <param name="claimedIssuer">The unverified issuer when the credential is a JSON Web Token carrying one; otherwise <see langword="null" />.</param>
    /// <returns><see langword="true" /> when an issuer was read; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// A <see langword="false" /> result covers everything that is not a JSON Web Token naming an issuer: an opaque
    /// credential such as an API key, a malformed token, a payload that is not JSON, and a token whose <c>iss</c> is
    /// absent or is not a string. None of them is distinguished, because the caller does the same thing with all of them.
    /// </remarks>
    public static bool TryReadClaimedIssuer(string? credential, [NotNullWhen(true)] out string? claimedIssuer)
    {
        claimedIssuer = null;

        return credential is not null && TryReadClaimedIssuer(credential.AsSpan(), out claimedIssuer);
    }

    /// <summary>Reads the issuer a compact-serialized JSON Web Token claims, from characters that are not a string.</summary>
    /// <param name="credential">The credential characters, which the caller may be holding outside the managed heap.</param>
    /// <param name="claimedIssuer">The unverified issuer when the characters are a JSON Web Token carrying one; otherwise <see langword="null" />.</param>
    /// <returns><see langword="true" /> when an issuer was read; otherwise <see langword="false" />.</returns>
    /// <remarks>Startup asks this of revealed API key material, which is held in a pinned buffer that is cleared afterwards, so the question has to be answerable without copying that material into a string the collector would move and leave behind.</remarks>
    public static bool TryReadClaimedIssuer(ReadOnlySpan<char> credential, [NotNullWhen(true)] out string? claimedIssuer)
    {
        claimedIssuer = null;

        return TrySplitSegments(credential, out _, out var encodedPayload)
            && TryReadStringMember(encodedPayload, IssuerClaimName, out claimedIssuer);
    }

    /// <summary>Reads the media type a compact-serialized JSON Web Token declares for itself.</summary>
    /// <param name="credential">The bearer credential a request presented.</param>
    /// <param name="declaredType">The unverified <c>typ</c> header when the credential is a JSON Web Token carrying one; otherwise <see langword="null" />.</param>
    /// <returns><see langword="true" /> when a declared type was read; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// The header rather than the payload, because that is where RFC 7519 puts <c>typ</c> and RFC 8725 section 3.11 asks
    /// a deployment reading more than one kind of token to state it. What it decides here is the same thing the issuer
    /// decides and no more: which handler judges the credential. That handler then verifies the signature and compares
    /// the declared type against the one it accepts, so a credential writing whatever it likes here picks which handler
    /// refuses it.
    /// </remarks>
    public static bool TryReadDeclaredType(string? credential, [NotNullWhen(true)] out string? declaredType)
    {
        declaredType = null;

        return credential is not null
            && TrySplitSegments(credential.AsSpan(), out var encodedHeader, out _)
            && TryReadStringMember(encodedHeader, TypeHeaderName, out declaredType);
    }

    /// <summary>Reads the key identifier a compact-serialized JSON Web Token names for the key that signed it.</summary>
    /// <param name="credential">The bearer credential a request presented.</param>
    /// <param name="keyId">The unverified <c>kid</c> header when the credential is a JSON Web Token carrying one; otherwise <see langword="null" />.</param>
    /// <returns><see langword="true" /> when a key identifier was read; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// What it decides is which stored public key the signature is checked against, and no more. The key it names is
    /// verified against afterwards, so a credential writing whatever it likes here picks which key refuses it — and one
    /// naming a key this deployment holds for another owner is refused by that key's own signature check rather than
    /// admitted as that owner.
    /// </remarks>
    public static bool TryReadKeyId(string? credential, [NotNullWhen(true)] out string? keyId)
    {
        keyId = null;

        return credential is not null
            && TrySplitSegments(credential.AsSpan(), out var encodedHeader, out _)
            && TryReadStringMember(encodedHeader, KeyIdHeaderName, out keyId);
    }

    /// <summary>Isolates the encoded header and payload of a compact serialization, which are the first two of its three segments.</summary>
    private static bool TrySplitSegments(
        ReadOnlySpan<char> credential,
        out ReadOnlySpan<char> encodedHeader,
        out ReadOnlySpan<char> encodedPayload)
    {
        encodedHeader = default;
        encodedPayload = default;

        var headerEnd = credential.IndexOf('.');
        if (headerEnd <= 0)
        {
            return false;
        }

        var payloadLength = credential[(headerEnd + 1)..].IndexOf('.');
        if (payloadLength <= 0)
        {
            return false;
        }

        var payloadEnd = headerEnd + 1 + payloadLength;

        // A compact serialization has exactly three segments. A fourth would make this a JSON Web Encryption, which is
        // not a shape this endpoint accepts, and reading its second segment would be reading an encrypted key.
        if (credential[(payloadEnd + 1)..].Contains('.'))
        {
            return false;
        }

        var header = credential[..headerEnd];
        var payload = credential.Slice(headerEnd + 1, payloadLength);

        if (header.Length > SegmentSizeLimitInBytes || payload.Length > SegmentSizeLimitInBytes)
        {
            return false;
        }

        encodedHeader = header;
        encodedPayload = payload;

        return true;
    }

    /// <summary>Decodes one segment and reads a string member out of the JSON object it carries.</summary>
    private static bool TryReadStringMember(
        ReadOnlySpan<char> encodedSegment,
        string memberName,
        [NotNullWhen(true)] out string? value)
    {
        value = null;

        // The status-returning overload rather than the Try one, which despite its name throws on a character that is
        // not base64url. Everything reaching here came from an unauthenticated request, so a malformed credential has to
        // be an ordinary refusal; letting it raise would answer a request that presented rubbish with a server fault.
        // The buffer is sized from the encoded length rather than through GetMaxDecodedLength, which throws in turn on a
        // length no base64url encoding can produce.
        var segment = new byte[(encodedSegment.Length / 4 * 3) + 3];

        var decoding = Base64Url.DecodeFromChars(encodedSegment, segment, out _, out var segmentLength);

        if (decoding != OperationStatus.Done)
        {
            return false;
        }

        try
        {
            using var members = JsonDocument.Parse(segment.AsMemory(0, segmentLength));

            if (members.RootElement.ValueKind is not JsonValueKind.Object
                || !members.RootElement.TryGetProperty(memberName, out var member)
                || member.ValueKind is not JsonValueKind.String)
            {
                return false;
            }

            value = member.GetString();

            return !string.IsNullOrEmpty(value);
        }
        catch (JsonException)
        {
            // Unparseable input from an unauthenticated caller is an ordinary refusal rather than a fault worth
            // reporting: the request simply selects no profile and receives the same answer as every other rejection.
            return false;
        }
    }
}

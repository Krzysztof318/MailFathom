// Copyright © 2026 Krzysztof Kasprowicz

using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace MailMcp.Infrastructure.Security;

/// <summary>Reads the issuer a JSON Web Token claims, before anything has been verified.</summary>
/// <remarks>
/// <para>
/// A deployment can trust several authorization servers, and the token itself is the only thing that says which one
/// issued it. Choosing a validator therefore has to read the token's own <c>iss</c> claim first — which is unsigned
/// input, chosen by whoever sent the request, and the name of this type says so at every call site.
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
    /// <summary>The largest encoded payload this reads, beyond which the token selects no profile and is refused.</summary>
    /// <remarks>An access token's payload is a few hundred bytes. The limit stops an unauthenticated request from making the host decode and parse an arbitrarily large document before anything has been verified.</remarks>
    private const int PayloadSizeLimitInBytes = 8 * 1024;

    private const string IssuerClaimName = "iss";

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

        if (!TryReadEncodedPayload(credential, out var encodedPayload))
        {
            return false;
        }

        // The status-returning overload rather than the Try one, which despite its name throws on a character that is
        // not base64url. Everything reaching here came from an unauthenticated request, so a malformed credential has to
        // be an ordinary refusal; letting it raise would answer a request that presented rubbish with a server fault.
        // The buffer is sized from the encoded length rather than through GetMaxDecodedLength, which throws in turn on a
        // length no base64url encoding can produce.
        var payload = new byte[(encodedPayload.Length / 4 * 3) + 3];

        var decoding = Base64Url.DecodeFromChars(encodedPayload, payload, out _, out var payloadLength);

        return decoding == OperationStatus.Done && TryReadIssuerClaim(payload.AsSpan(0, payloadLength), out claimedIssuer);
    }

    /// <summary>Isolates the encoded payload of a compact serialization, which is the second of its three segments.</summary>
    private static bool TryReadEncodedPayload(ReadOnlySpan<char> credential, out ReadOnlySpan<char> encodedPayload)
    {
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

        var payload = credential.Slice(headerEnd + 1, payloadLength);

        if (payload.Length > PayloadSizeLimitInBytes)
        {
            return false;
        }

        encodedPayload = payload;

        return true;
    }

    private static bool TryReadIssuerClaim(ReadOnlySpan<byte> payload, [NotNullWhen(true)] out string? claimedIssuer)
    {
        claimedIssuer = null;

        try
        {
            using var claims = JsonDocument.Parse(payload.ToArray());

            if (claims.RootElement.ValueKind is not JsonValueKind.Object
                || !claims.RootElement.TryGetProperty(IssuerClaimName, out var issuer)
                || issuer.ValueKind is not JsonValueKind.String)
            {
                return false;
            }

            claimedIssuer = issuer.GetString();

            return !string.IsNullOrEmpty(claimedIssuer);
        }
        catch (JsonException)
        {
            // Unparseable input from an unauthenticated caller is an ordinary refusal rather than a fault worth
            // reporting: the request simply selects no profile and receives the same answer as every other rejection.
            return false;
        }
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace MailFathom.Infrastructure.Security.Passwords;

/// <summary>Reads the user-id and password out of an HTTP <c>Authorization: Basic</c> header.</summary>
/// <remarks>
/// <para>
/// RFC 7617 carries both halves of the credential in one Base64 field, separated by the first colon, so reading one is
/// a decode and a split rather than a parse — and every one of the ways it can go wrong has to be refused before any
/// password verification is attempted. Verification is deliberately expensive, so a header that could never have been a
/// credential must cost the sender the price of a string comparison rather than the price of a key derivation.
/// </para>
/// <para>
/// Everything is bounded before it is decoded. A header longer than <see cref="MaximumEncodedLength" /> is refused
/// without allocating, which is what stops an unauthenticated caller from making this process decode a megabyte per
/// request; the limit is set from the longest credential this deployment can actually hold, so nothing an operator can
/// legitimately provision comes near it.
/// </para>
/// <para>
/// The decoding is UTF-8 and is strict. RFC 7617 leaves the encoding of the credential unstated for historical reasons
/// and adds a <c>charset</c> parameter naming UTF-8 as the one a modern server should ask for, which is what the
/// challenge this deployment writes does. Decoding strictly means a sequence that is not UTF-8 is refused rather than
/// silently turned into replacement characters, so two different byte sequences can never fold into one password.
/// </para>
/// <para>
/// The decoded octets and the decoded characters both live in pinned buffers this reader owns and are cleared before it
/// returns, and the credential it hands back owns the one buffer that survives. The header value itself arrives as a
/// <see cref="string" /> the request pipeline already materialized and which cannot be erased, which is a property of
/// HTTP rather than something this can improve — what it controls is every copy after that one.
/// </para>
/// </remarks>
public static class BasicCredentialHeader
{
    /// <summary>The HTTP authentication scheme this reader accepts, and the one a challenge names.</summary>
    public const string HttpAuthenticationScheme = "Basic";

    /// <summary>The longest header value this reader will decode, in characters.</summary>
    /// <remarks>
    /// The longest username and the longest password this deployment stores, encoded as UTF-8 with room for characters
    /// outside the basic plane and then Base64-expanded, with the whole rounded up. A credential this deployment could
    /// have issued always fits; anything past it was never one.
    /// </remarks>
    public const int MaximumEncodedLength = 2048;

    private const char UserIdSeparator = ':';

    /// <summary>Reads the credential an <c>Authorization</c> header value carried.</summary>
    /// <param name="authorizationHeaderValue">The raw header value, or <see langword="null" /> when the request carried none.</param>
    /// <param name="credential">The credential when the header carried a readable one; otherwise <see langword="null" />.</param>
    /// <returns><see langword="true" /> when the header carried a Basic credential this deployment could judge.</returns>
    /// <remarks>
    /// The scheme is matched ignoring case, as HTTP requires, and at least one space separates it from the credential.
    /// A header naming another scheme, naming this one with nothing after it, carrying something that is not Base64,
    /// decoding to something that is not UTF-8, or decoding to a value with no colon in it is not a credential and is
    /// refused identically — the caller answers every one of them with the same challenge.
    /// </remarks>
    public static bool TryRead(
        string? authorizationHeaderValue,
        [NotNullWhen(true)] out PresentedBasicCredential? credential)
    {
        credential = null;

        if (authorizationHeaderValue is null || authorizationHeaderValue.Length > MaximumEncodedLength)
        {
            return false;
        }

        var headerValue = authorizationHeaderValue.AsSpan().Trim();

        if (!headerValue.StartsWith(HttpAuthenticationScheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var encodedCredential = headerValue[HttpAuthenticationScheme.Length..];

        // A scheme immediately followed by its credential is not the syntax: RFC 7235 separates the two with at least
        // one space, and a value that lost none of its length to trimming had no space to lose.
        if (encodedCredential.TrimStart(' ').Length == encodedCredential.Length || encodedCredential.IsWhiteSpace())
        {
            return false;
        }

        return TryDecode(encodedCredential.TrimStart(' '), out credential);
    }

    /// <summary>Decodes the Base64 field into the two halves it carries.</summary>
    private static bool TryDecode(
        ReadOnlySpan<char> encodedCredential,
        [NotNullWhen(true)] out PresentedBasicCredential? credential)
    {
        credential = null;

        var decodedOctets = GC.AllocateArray<byte>(((encodedCredential.Length / 4) + 1) * 3, pinned: true);

        try
        {
            if (!Convert.TryFromBase64Chars(encodedCredential, decodedOctets, out var octetsWritten)
                || octetsWritten == 0)
            {
                return false;
            }

            return TrySplit(decodedOctets.AsSpan(0, octetsWritten), out credential);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decodedOctets);
        }
    }

    /// <summary>Turns the decoded octets into a user-id and a password, at the first colon.</summary>
    /// <remarks>
    /// The first colon rather than the last, as RFC 7617 requires: a user-id may not contain one, and a password may
    /// contain as many as it likes. Splitting anywhere else would let a password's own colon move the boundary and
    /// authenticate a different name.
    /// </remarks>
    private static bool TrySplit(
        ReadOnlySpan<byte> decodedOctets,
        [NotNullWhen(true)] out PresentedBasicCredential? credential)
    {
        credential = null;

        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var decodedText = GC.AllocateArray<char>(decodedOctets.Length, pinned: true);
        var charactersWritten = 0;

        try
        {
            charactersWritten = strictUtf8.GetChars(decodedOctets, decodedText);
        }
        catch (DecoderFallbackException)
        {
            decodedText.AsSpan().Clear();

            return false;
        }

        var separator = decodedText.AsSpan(0, charactersWritten).IndexOf(UserIdSeparator);

        if (separator < 0)
        {
            decodedText.AsSpan().Clear();

            return false;
        }

        // The user-id is a string because it is not a secret and is about to be canonicalized into one anyway; the
        // password stays in the pinned buffer, which the credential owns and clears.
        credential = new PresentedBasicCredential(
            new string(decodedText.AsSpan(0, separator)),
            decodedText,
            separator + 1,
            charactersWritten - separator - 1);

        return true;
    }
}

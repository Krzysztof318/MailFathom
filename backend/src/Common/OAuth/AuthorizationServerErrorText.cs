// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Common.OAuth;

/// <summary>Reduces an authorization server's <c>error</c> value to a form that is safe to put in a message.</summary>
/// <remarks>
/// <para>
/// The value is the one useful thing a rejected grant returns — it separates a revoked refresh token from a mistyped
/// client secret — and it is also attacker-influenced text from a machine this process does not own. RFC 6749 gives it
/// a character set and no length bound at all, so a replaced or misconfigured server can answer with kilobytes of
/// anything and have it copied into an operator's log through an exception message.
/// </para>
/// <para>
/// Sanitizing rather than rejecting keeps the diagnostic: an unrecognized code from a provider that invented one is
/// still worth showing, so what is removed is the ability to inject line breaks, control characters, or bulk. A value
/// that survives none of it reads as <c>unspecified</c>, which is accurate — the server said nothing usable.
/// </para>
/// </remarks>
public static class AuthorizationServerErrorText
{
    /// <summary>What a value that carried no usable characters is reported as.</summary>
    private const string Unspecified = "unspecified";

    /// <summary>The longest code kept, past which the value is truncated.</summary>
    /// <remarks>The longest code RFC 6749 and RFC 8628 define is <c>unsupported_grant_type</c> at 22 characters; the bound leaves generous room for a provider-specific one while keeping a log line a log line.</remarks>
    private const int MaximumLength = 64;

    /// <summary>Reduces a server-supplied error code to printable, single-line text of bounded length.</summary>
    /// <param name="serverSuppliedvalue">The <c>error</c> member of the response, which may be anything.</param>
    /// <returns>The sanitized code, or <c>unspecified</c> when nothing usable remained.</returns>
    public static string Sanitize(string? serverSuppliedvalue)
    {
        if (string.IsNullOrWhiteSpace(serverSuppliedvalue))
        {
            return Unspecified;
        }

        // Control characters are what would break a log line into two and let the rest of the value read as a record
        // of its own; anything non-ASCII is outside the character set RFC 6749 defines for this member.
        var printableCharacters = serverSuppliedvalue
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.' or ':')
            .Take(MaximumLength)
            .ToArray();

        return printableCharacters.Length == 0 ? Unspecified : new string(printableCharacters);
    }
}

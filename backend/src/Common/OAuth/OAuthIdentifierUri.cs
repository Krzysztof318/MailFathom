// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Common.OAuth;

/// <summary>The shape OAuth requires of a URL used as a name rather than as something to fetch.</summary>
/// <remarks>
/// <para>
/// Two settings on the MCP endpoint carry such a URL, and both specifications constrain the shape identically. An
/// authorization server's issuer identifier is an <c>https</c> URL with no query and no fragment (RFC 8414 section 2),
/// and the canonical resource identifier a token's audience is bound to is an absolute URI without a fragment (RFC 8707
/// section 2, which the MCP authorization specification adopts). The shape is therefore one check used by both.
/// </para>
/// <para>
/// What the two do not share is whether the value may be rewritten, which is why <see cref="IsWellFormed" /> and
/// <see cref="TryCanonicalize" /> are separate. Both values end up in an exact string comparison — an issuer against a
/// token's <c>iss</c>, a resource against its <c>aud</c> — and the difference is who writes the other side.
/// </para>
/// <para>
/// The resource is named by MailFathom itself: it is published in the protected resource metadata document, a client copies
/// it into the <c>resource</c> parameter, and the authorization server puts it back in the token. Every appearance
/// originates here, so bringing it to one canonical form makes two spellings of it impossible.
/// </para>
/// <para>
/// An issuer is the opposite. The authorization server emits <c>iss</c> in a form it chose, MailFathom only recognizes it,
/// and several widely deployed servers publish an issuer whose path is a single trailing slash. Canonicalizing that away
/// would leave a configuration that looks right and refuses every token the server issues, so an issuer is validated for
/// shape and then compared exactly as the operator copied it from the server.
/// </para>
/// <para>
/// A query component is refused in both cases even though RFC 8707 merely discourages it. An identifier carrying one is
/// either a mistake or an attempt to build two identifiers a careless comparison would read as one, and neither is worth
/// accepting for a value an operator writes once.
/// </para>
/// </remarks>
public static class OAuthIdentifierUri
{
    /// <summary>Reports whether a configured value has the shape OAuth requires of an identifier.</summary>
    /// <param name="configuredValue">The configured identifier, for example <c>https://sso.example.test/realms/mailfathom</c>.</param>
    /// <returns><see langword="true" /> when the value is usable as an OAuth identifier; otherwise <see langword="false" />.</returns>
    public static bool IsWellFormed([NotNullWhen(true)] string? configuredValue) =>
        TryReadIdentifier(configuredValue, out _);

    /// <summary>Brings a configured identifier to the one form everything else compares against.</summary>
    /// <param name="configuredValue">The configured identifier.</param>
    /// <param name="canonicalIdentifier">The canonical identifier when the value is accepted; otherwise an empty string.</param>
    /// <returns><see langword="true" /> when the value is usable as an OAuth identifier; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// The scheme and host are lowercased and a default port is dropped, because neither carries meaning in a URL and the
    /// MCP authorization specification asks an implementation to accept an uppercase scheme or host for robustness. A
    /// trailing slash is dropped where the path is empty, which is the form that specification asks implementations to
    /// settle on; on a path that identifies something, a trailing slash is part of what it identifies and is left alone.
    /// Use this for a value MailFathom itself publishes, never for one an authorization server emits.
    /// </remarks>
    public static bool TryCanonicalize(string? configuredValue, out string canonicalIdentifier)
    {
        canonicalIdentifier = string.Empty;

        if (!TryReadIdentifier(configuredValue, out var identifier))
        {
            return false;
        }

        var authority = identifier.IsDefaultPort
            ? identifier.Host
            : $"{identifier.Host}:{identifier.Port}";

        var path = identifier.AbsolutePath is "/" ? string.Empty : identifier.AbsolutePath;

        canonicalIdentifier = $"{identifier.Scheme}://{authority}{path}";

        return true;
    }

    /// <summary>Brings a configured identifier that has already been validated to its canonical form.</summary>
    /// <param name="configuredValue">The configured identifier.</param>
    /// <returns>The canonical identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when the value was never validated and is not usable as an OAuth identifier.</exception>
    public static string Canonicalize(string? configuredValue) =>
        TryCanonicalize(configuredValue, out var canonicalIdentifier)
            ? canonicalIdentifier
            : throw new ArgumentException(
                "The identifier was canonicalized before it was validated, so it is not usable as an OAuth identifier.",
                nameof(configuredValue));

    private static bool TryReadIdentifier(string? configuredValue, [NotNullWhen(true)] out Uri? identifier)
    {
        identifier = null;

        if (string.IsNullOrWhiteSpace(configuredValue)
            || !Uri.TryCreate(configuredValue.Trim(), UriKind.Absolute, out var parsedIdentifier))
        {
            return false;
        }

        var isUsableIdentifier = parsedIdentifier.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrEmpty(parsedIdentifier.Host)
            && string.IsNullOrEmpty(parsedIdentifier.Query)
            && string.IsNullOrEmpty(parsedIdentifier.Fragment)
            && string.IsNullOrEmpty(parsedIdentifier.UserInfo);

        if (!isUsableIdentifier)
        {
            return false;
        }

        identifier = parsedIdentifier;

        return true;
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Common.ClientAssertions;
using MailFathom.Infrastructure.Security.OAuth;
using MailFathom.Infrastructure.Security.Passwords;

namespace MailFathom.Host.Security.Transport;

/// <summary>Decides which handler judges the credential a request presented.</summary>
/// <remarks>
/// <para>
/// A username and password are the one credential that names its own scheme in the header, so they are recognized first
/// and by that name alone: RFC 7617 writes <c>Basic</c> where every other credential here writes <c>Bearer</c>, and no
/// bearer credential can be mistaken for one.
/// </para>
/// <para>
/// Every other credential arrives as an HTTP bearer credential, so the endpoint has to tell those apart before any of
/// them can be checked. The shape does it, in the order the credentials are self-describing. A client assertion declares
/// its own media type in its header, which nothing else this endpoint accepts does, so it is recognized next and
/// exactly. An access token is a compact-serialized JSON Web Token naming its issuer, so a request carrying one from a
/// configured authorization server reaches that server's validator. An API key is an opaque string that is neither, and
/// everything left reaches the key comparison.
/// </para>
/// <para>
/// The declared type and the issuer are both read unverified, and choosing a handler is the only thing either decides.
/// That handler then checks the signature against its own keys and compares what the credential claims against its own
/// configuration, so a request that writes whatever it likes in either place picks which handler refuses it and nothing
/// more. A token naming an issuer nobody configured matches no profile, and an unknown issuer therefore never selects a
/// validator rather than selecting a lenient one.
/// </para>
/// <para>
/// One consequence is worth stating, because it is the shape of a configuration mistake rather than of an attack: a key
/// configured under <c>ApiKey</c> whose material happens to declare MailFathom's own assertion type would be routed to
/// the assertion scheme and never compared. That is the same trap a token-shaped API key already falls into with a
/// configured authorization server, and the same remedy applies — an API key is issued opaque.
/// </para>
/// <para>
/// Selection is total and deterministic: every request reaches exactly one registered scheme, and the same request
/// always reaches the same one. Where nothing matches, the request reaches a scheme that authenticates nobody and
/// answers with the challenge — which is the same outcome as a refusal, reached without a special case.
/// </para>
/// </remarks>
internal sealed class CredentialSchemeSelector
{
    private readonly Dictionary<string, string> oauthSchemesByIssuer;
    private readonly string? apiKeySchemeName;
    private readonly string? clientAssertionSchemeName;
    private readonly string? basicSchemeName;
    private readonly string unmatchedSchemeName;

    /// <summary>Initializes a new selector.</summary>
    /// <param name="oauthSchemesByIssuer">The scheme validating each configured authorization server's tokens, keyed by that server's issuer.</param>
    /// <param name="apiKeySchemeName">The scheme comparing API keys, or <see langword="null" /> when API keys are not accepted.</param>
    /// <param name="clientAssertionSchemeName">The scheme verifying client assertions, or <see langword="null" /> when assertions are not accepted.</param>
    /// <param name="basicSchemeName">The scheme judging an owner's username and password, or <see langword="null" /> when passwords are not accepted.</param>
    /// <param name="unmatchedSchemeName">The scheme a request reaches when no credential it presented selects one.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="oauthSchemesByIssuer" /> or <paramref name="unmatchedSchemeName" /> is <see langword="null" />.</exception>
    internal CredentialSchemeSelector(
        IReadOnlyDictionary<string, string> oauthSchemesByIssuer,
        string? apiKeySchemeName,
        string? clientAssertionSchemeName,
        string? basicSchemeName,
        string unmatchedSchemeName)
    {
        ArgumentNullException.ThrowIfNull(oauthSchemesByIssuer);
        ArgumentNullException.ThrowIfNull(unmatchedSchemeName);

        this.oauthSchemesByIssuer = new Dictionary<string, string>(oauthSchemesByIssuer, StringComparer.Ordinal);
        this.apiKeySchemeName = apiKeySchemeName;
        this.clientAssertionSchemeName = clientAssertionSchemeName;
        this.basicSchemeName = basicSchemeName;
        this.unmatchedSchemeName = unmatchedSchemeName;
    }

    /// <summary>Reports which scheme judges the credential an <c>Authorization</c> header carried.</summary>
    /// <param name="authorizationHeaderValue">The raw header value, empty when the request carried none.</param>
    /// <returns>The name of a registered authentication scheme.</returns>
    internal string SchemeFor(string? authorizationHeaderValue)
    {
        // Named rather than shaped, and read off the raw header rather than off a parsed credential: what selects this
        // scheme is the word the request wrote, so a value that says Basic and decodes to nothing usable is refused by
        // the handler that understands the method instead of falling through to the key comparison.
        if (this.basicSchemeName is { } passwordSchemeName && NamesTheBasicScheme(authorizationHeaderValue))
        {
            return passwordSchemeName;
        }

        if (!BearerCredentialHeader.TryRead(authorizationHeaderValue, out var credential))
        {
            return this.apiKeySchemeName ?? this.unmatchedSchemeName;
        }

        if (this.clientAssertionSchemeName is { } assertionSchemeName
            && UnverifiedJsonWebToken.TryReadDeclaredType(credential, out var declaredType)
            && string.Equals(declaredType, ClientAssertion.DeclaredType, StringComparison.Ordinal))
        {
            return assertionSchemeName;
        }

        if (UnverifiedJsonWebToken.TryReadClaimedIssuer(credential, out var claimedIssuer)
            && this.oauthSchemesByIssuer.TryGetValue(claimedIssuer, out var oauthSchemeName))
        {
            return oauthSchemeName;
        }

        return this.apiKeySchemeName ?? this.unmatchedSchemeName;
    }

    /// <summary>Reports whether the header's own first word is the password scheme, rather than merely beginning with its letters.</summary>
    /// <remarks>The scheme is a token, so what ends it is whitespace or the end of the value; matching the prefix alone would hand a header naming some future <c>BasicSomething</c> scheme to the handler that judges passwords.</remarks>
    private static bool NamesTheBasicScheme(string? authorizationHeaderValue)
    {
        if (authorizationHeaderValue is null)
        {
            return false;
        }

        var value = authorizationHeaderValue.AsSpan().TrimStart();

        if (!value.StartsWith(BasicCredentialHeader.HttpAuthenticationScheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = value[BasicCredentialHeader.HttpAuthenticationScheme.Length..];

        return rest.IsEmpty || rest[0] is ' ' or '\t';
    }
}

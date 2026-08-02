// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Security;

namespace MailFathom.Host.Security;

/// <summary>Decides which handler judges the credential a request presented.</summary>
/// <remarks>
/// <para>
/// Both credentials arrive as an HTTP bearer credential, so the endpoint has to tell an API key from an access token
/// before either can be checked. The shape does it: an access token is a compact-serialized JSON Web Token naming its
/// issuer, and an API key is an opaque string that is not one. A request carrying a token from a configured
/// authorization server reaches that server's validator; everything else reaches the API key comparison.
/// </para>
/// <para>
/// The issuer read out of the token is unverified, and choosing a validator is the only thing it decides. That validator
/// then checks the signature against its own key set and compares the issuer against its own configuration, so a request
/// that writes whatever it likes there picks which handler refuses it and nothing more. A token naming an issuer nobody
/// configured matches no profile, and an unknown issuer therefore never selects a validator rather than selecting a
/// lenient one.
/// </para>
/// <para>
/// Selection is total and deterministic: every request reaches exactly one registered scheme, and the same request
/// always reaches the same one. Where nothing matches, the request reaches a scheme that authenticates nobody and
/// answers with the challenge — which is the same outcome as a refusal, reached without a special case.
/// </para>
/// </remarks>
internal sealed class McpCredentialSchemeSelector
{
    private readonly Dictionary<string, string> oauthSchemesByIssuer;
    private readonly string? apiKeySchemeName;
    private readonly string unmatchedSchemeName;

    /// <summary>Initializes a new selector.</summary>
    /// <param name="oauthSchemesByIssuer">The scheme validating each configured authorization server's tokens, keyed by that server's issuer.</param>
    /// <param name="apiKeySchemeName">The scheme comparing API keys, or <see langword="null" /> when API keys are not accepted.</param>
    /// <param name="unmatchedSchemeName">The scheme a request reaches when no credential it presented selects one.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="oauthSchemesByIssuer" /> or <paramref name="unmatchedSchemeName" /> is <see langword="null" />.</exception>
    internal McpCredentialSchemeSelector(
        IReadOnlyDictionary<string, string> oauthSchemesByIssuer,
        string? apiKeySchemeName,
        string unmatchedSchemeName)
    {
        ArgumentNullException.ThrowIfNull(oauthSchemesByIssuer);
        ArgumentNullException.ThrowIfNull(unmatchedSchemeName);

        this.oauthSchemesByIssuer = new Dictionary<string, string>(oauthSchemesByIssuer, StringComparer.Ordinal);
        this.apiKeySchemeName = apiKeySchemeName;
        this.unmatchedSchemeName = unmatchedSchemeName;
    }

    /// <summary>Reports which scheme judges the credential an <c>Authorization</c> header carried.</summary>
    /// <param name="authorizationHeaderValue">The raw header value, empty when the request carried none.</param>
    /// <returns>The name of a registered authentication scheme.</returns>
    internal string SchemeFor(string? authorizationHeaderValue)
    {
        if (BearerCredentialHeader.TryRead(authorizationHeaderValue, out var credential)
            && UnverifiedJsonWebToken.TryReadClaimedIssuer(credential, out var claimedIssuer)
            && this.oauthSchemesByIssuer.TryGetValue(claimedIssuer, out var oauthSchemeName))
        {
            return oauthSchemeName;
        }

        return this.apiKeySchemeName ?? this.unmatchedSchemeName;
    }
}

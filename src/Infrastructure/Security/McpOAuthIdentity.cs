// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;

namespace MailFathom.Infrastructure.Security;

/// <summary>The identity MailFathom keeps from a validated access token, and what it is allowed to be asked.</summary>
/// <remarks>
/// <para>
/// A validated token routinely carries a name, an email address, a set of groups, a tenant, and whatever else the
/// authorization server was configured to include. None of it is copied here. What survives validation is who the token
/// is for, who said so, and which scopes it carries — because those are the only three facts anything downstream acts
/// on, and a claim that is present is a claim something will eventually be tempted to trust.
/// </para>
/// <para>
/// The identity is <c>iss</c> together with <c>sub</c>, never <c>sub</c> alone. A subject identifier is only unique
/// within the server that issued it, so two authorization servers can both name a subject <c>1</c> without either being
/// wrong; pairing it with the issuer is what stops a deployment trusting two servers from merging their populations. It
/// is also deliberately not an email address: an address is reassignable and a mailbox belongs to whoever holds it
/// today, whereas <c>sub</c> is what the server promises will not be reused.
/// </para>
/// <para>
/// Scopes are read from both spellings in circulation. RFC 9068 defines <c>scope</c> as a space-delimited string, and
/// several servers emit <c>scp</c> instead, sometimes repeated rather than delimited. Reading both is not a
/// provider-specific branch: nothing here asks which server sent the token, and a server emitting neither simply carries
/// no scopes.
/// </para>
/// </remarks>
public static class McpOAuthIdentity
{
    /// <summary>The claim type carrying the stable identity of the person a request was authorized by.</summary>
    /// <remarks>Its value is the issuer and the subject joined by <c>|</c>, a character an <c>https</c> identifier cannot contain unescaped, so the pair cannot be read back ambiguously.</remarks>
    public const string SubjectClaimType = "urn:mailfathom:oauth-subject";

    /// <summary>The claim type carrying the issuer that authenticated the person, which is half of their identity.</summary>
    public const string IssuerClaimType = "urn:mailfathom:oauth-issuer";

    /// <summary>The claim type carrying one scope the validated token granted.</summary>
    public const string ScopeClaimType = "urn:mailfathom:oauth-scope";

    /// <summary>The claim type a role check reads on an identity this produces, which nothing ever issues.</summary>
    /// <remarks>
    /// Named rather than left empty, because an empty role type is not the absence of one: <see cref="ClaimsIdentity" />
    /// restores the framework's default when it is given one, and the token validator refuses it outright. A claim type
    /// no mapping ever writes is what actually makes a role check answer no, whatever an authorization server put in the
    /// token.
    /// </remarks>
    public const string RoleClaimType = "urn:mailfathom:oauth-role";

    private const char IdentitySeparator = '|';

    private static readonly string[] TokenScopeClaimTypes = ["scope", "scp"];

    /// <summary>Joins an issuer and a subject into the one identity everything else compares against.</summary>
    /// <param name="issuer">The authorization server that authenticated the person.</param>
    /// <param name="subject">That server's own stable identifier for them.</param>
    /// <returns>The identity carried by <see cref="SubjectClaimType" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="issuer" /> or <paramref name="subject" /> is <see langword="null" />.</exception>
    /// <remarks>Composed in one place so a configured identity and a token's identity cannot be spelled differently; a subject is unique only within its issuer, so neither half identifies anyone alone.</remarks>
    public static string IdentityOf(string issuer, string subject)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(subject);

        return $"{issuer}{IdentitySeparator}{subject}";
    }

    /// <summary>Maps a validated token's claims onto the minimal identity MailFathom carries.</summary>
    /// <param name="validatedClaims">The claims of a token whose signature, issuer, audience, and lifetime have already been checked.</param>
    /// <param name="authenticationScheme">The scheme that validated the token, which the identity records as its authentication type.</param>
    /// <returns>The identity, or <see langword="null" /> when the token names no subject and therefore authorizes nobody.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="validatedClaims" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A token with no <c>sub</c> is refused rather than mapped to an anonymous identity. It is a valid token — a client
    /// credentials grant produces one — but it names no person, and this endpoint's whole authorization story is which
    /// person is asking. Accepting it would make "authenticated" mean two different things.
    /// </remarks>
    public static ClaimsIdentity? FromValidatedToken(IEnumerable<Claim> validatedClaims, string authenticationScheme)
    {
        ArgumentNullException.ThrowIfNull(validatedClaims);

        var claims = validatedClaims.ToArray();

        var issuer = SingleClaimValue(claims, "iss");
        var subject = SingleClaimValue(claims, "sub");

        if (issuer is null || subject is null)
        {
            return null;
        }

        Claim[] mappedClaims =
        [
            new(SubjectClaimType, IdentityOf(issuer, subject)),
            new(IssuerClaimType, issuer),
            .. ScopesOf(claims).Select(scope => new Claim(ScopeClaimType, scope)),
        ];

        // The role type names a claim nothing maps, because nothing maps a token claim onto a role. Leaving the
        // framework's default in place would let a claim named 'role' arriving from an authorization server answer an
        // IsInRole check that no configuration ever authorized.
        return new ClaimsIdentity(mappedClaims, authenticationScheme, SubjectClaimType, RoleClaimType);
    }

    /// <summary>Reports which person a principal is, when a validated token produced it.</summary>
    /// <param name="principal">The principal a validated credential produced.</param>
    /// <returns>The issuer and subject pair, or <see langword="null" /> when no token produced this principal.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="principal" /> is <see langword="null" />.</exception>
    public static string? IdentityCarriedBy(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal.FindFirst(SubjectClaimType)?.Value;
    }

    /// <summary>Reports whether an authenticated principal carries every scope a request requires.</summary>
    /// <param name="principal">The principal a validated credential produced.</param>
    /// <param name="requiredScopes">The scopes the deployment requires, empty when any valid token suffices.</param>
    /// <returns><see langword="true" /> when every required scope is present; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="principal" /> or <paramref name="requiredScopes" /> is <see langword="null" />.</exception>
    public static bool CarriesEveryScope(ClaimsPrincipal principal, IReadOnlyCollection<string> requiredScopes)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(requiredScopes);

        if (requiredScopes.Count == 0)
        {
            return true;
        }

        var grantedScopes = principal
            .FindAll(ScopeClaimType)
            .Select(scope => scope.Value)
            .ToHashSet(StringComparer.Ordinal);

        return requiredScopes.All(grantedScopes.Contains);
    }

    /// <summary>Reads the scopes a validated token granted, from either spelling and either encoding.</summary>
    private static IEnumerable<string> ScopesOf(IEnumerable<Claim> claims) => claims
        .Where(claim => TokenScopeClaimTypes.Contains(claim.Type, StringComparer.Ordinal))
        .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .Distinct(StringComparer.Ordinal);

    /// <summary>Reads a claim that must appear exactly once, treating a repeated one as absent.</summary>
    /// <remarks>
    /// A token carrying two <c>iss</c> or two <c>sub</c> claims is malformed, and picking either would let whichever the
    /// enumeration happened to reach first decide who the request is. Both are rejected instead.
    /// </remarks>
    private static string? SingleClaimValue(IEnumerable<Claim> claims, string claimType)
    {
        var values = claims
            .Where(claim => string.Equals(claim.Type, claimType, StringComparison.Ordinal))
            .Take(2)
            .ToArray();

        return values is [{ Value: var value }] && !string.IsNullOrWhiteSpace(value) ? value : null;
    }
}

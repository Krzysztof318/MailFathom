// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Common.ClientAssertions;
using MailFathom.Host.Security.Mcp;
using Microsoft.IdentityModel.Tokens;

namespace MailFathom.Host.Security.ClientAssertions;

/// <summary>The rules a client assertion is judged by, whichever surface it was presented to.</summary>
/// <remarks>
/// <para>
/// They are constants rather than settings, for the reason every rule in <see cref="OAuthTokenValidation" /> is one:
/// each is a security decision with a single defensible answer, and a deployment that could weaken it would eventually
/// be a deployment that had. The permitted algorithms are literally that type's list rather than a second one, so the
/// deployment cannot end up accepting a signature on an assertion that it would refuse on an access token.
/// </para>
/// <para>
/// What is asked of an assertion is narrower than what is asked of a token, and the difference is where each credential
/// comes from. A token is issued by a server that decides for itself who receives one, so the endpoint checks an issuer,
/// a subject, and a set of scopes. An assertion is minted by a client the operator registered a key for, so the
/// registration is the authorization and what remains to check is that this credential is genuinely that client's, is
/// for this surface, and has not been used before.
/// </para>
/// </remarks>
internal static class ClientAssertionValidation
{
    /// <summary>How much disagreement between this host's clock and the client's is tolerated.</summary>
    /// <remarks>The same window an access token is judged by, because it answers the same question about the same two machines. It widens the permitted lifetime by its own length, which is why the bound below is stated against it.</remarks>
    internal static readonly TimeSpan PermittedClockSkew = OAuthTokenValidation.PermittedClockSkew;

    /// <summary>The furthest ahead an assertion's expiry may sit when it is verified.</summary>
    /// <remarks>
    /// The permitted lifetime plus the tolerated clock disagreement, because a client whose clock runs fast writes an
    /// expiry further ahead than it meant to and would otherwise be refused for the endpoint's own tolerance. It is what
    /// makes the credential short-lived in fact rather than by convention: an assertion claiming a day's validity is
    /// refused however correctly it is signed.
    /// </remarks>
    internal static readonly TimeSpan FurthestPermittedExpiry = ClientAssertion.MaximumLifetime + PermittedClockSkew;

    /// <summary>States what an assertion presented to one surface must satisfy to be accepted.</summary>
    /// <param name="audience">The audience the surface publishes, which the assertion must name.</param>
    /// <param name="verificationKeys">The configured public keys the signature may be verified against.</param>
    /// <param name="timeProvider">The clock the assertion's own window is judged against.</param>
    /// <returns>The validation rules.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The keys are handed over through a resolver returning all of them rather than through the signing-key collection,
    /// so which keys are tried is stated here instead of following from whether the assertion named one. A client that
    /// wrote a key identifier, wrote somebody else's, or wrote none at all reaches exactly the same set — which is what
    /// keeps the credential's own contents from deciding what it is checked against.
    /// </para>
    /// <para>
    /// No issuer is validated, because an assertion names none: the client's identity here is the key that signed it and
    /// the name the operator configured that key under, not a string inside the credential. Turning issuer validation on
    /// against a value the credential supplies would be asking the credential to vouch for itself.
    /// </para>
    /// </remarks>
    [SuppressMessage(
        "Security",
        "CA5404:Do not disable token validation checks",
        Justification = "An assertion names no issuer, because the client's identity here is the key that signed it and "
            + "the name the operator configured that key under. The rule's premise is a token whose issuer identifies "
            + "who produced it; validating one against a value this credential supplies would be asking the credential "
            + "to vouch for itself, and the signature check the resolver above feeds is what actually establishes the "
            + "caller.")]
    internal static TokenValidationParameters ParametersFor(
        string audience,
        IReadOnlyList<SecurityKey> verificationKeys,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(audience);
        ArgumentNullException.ThrowIfNull(verificationKeys);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new TokenValidationParameters
        {
            ValidateIssuer = false,

            // The audience is the surface, which is what refuses an assertion minted to read a mailbox at the endpoint
            // that administers the service. There is one spelling of it and a client copies it verbatim, so there is no
            // second form to be lenient towards.
            ValidateAudience = true,
            ValidAudience = audience,
            IgnoreTrailingSlashWhenValidatingAudience = false,

            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = PermittedClockSkew,

            // The window is judged against the injected clock rather than the framework's own, which reads the machine
            // directly. It is the same clock the key's configured lifetime and the permitted-expiry bound are read from,
            // so one credential is never judged by two clocks — and a test can state what "now" is instead of arranging
            // for the wall clock to make its case.
            LifetimeValidator = (notBefore, expires, _, _) => IsInsideItsWindow(notBefore, expires, timeProvider),

            RequireSignedTokens = true,
            IssuerSigningKeyResolver = (_, _, _, _) => verificationKeys,
            ValidAlgorithms = OAuthTokenValidation.PermittedSignatureAlgorithms,

            // The declared type is what separates this credential from an access token, so it is required rather than
            // merely permitted: nothing else may be presented here, and this may be presented nowhere else.
            ValidTypes = [ClientAssertion.DeclaredType],

            // Neither is ever issued on an assertion's identity, which carries the configured key's name and nothing
            // else. Naming claim types nothing writes is what makes a name lookup and a role check answer nothing rather
            // than fall back to whatever the framework would have read.
            NameClaimType = ClientAssertionAuthentication.KeyNameClaimType,
            RoleClaimType = ClientAssertionAuthentication.RoleClaimType,
        };
    }

    /// <summary>Reports whether an assertion is inside the window it claims for itself.</summary>
    /// <remarks>
    /// An absent expiry is refused rather than read as unbounded, which is what <c>RequireExpirationTime</c> would have
    /// said had the framework's own lifetime check still been running. A start time is optional, because an assertion is
    /// minted for the request it accompanies and has no reason to name one; where a client writes one anyway it is
    /// honoured.
    /// </remarks>
    private static bool IsInsideItsWindow(DateTime? notBefore, DateTime? expires, TimeProvider timeProvider)
    {
        if (expires is not { } expiresAt)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        return now < expiresAt + PermittedClockSkew
            && (notBefore is not { } startsAt || now >= startsAt - PermittedClockSkew);
    }
}

// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Security;
using Microsoft.IdentityModel.Tokens;

namespace MailMcp.Host.Security;

/// <summary>The constants the OAuth side of the MCP endpoint is composed from.</summary>
/// <remarks>
/// They are constants rather than settings on purpose. Each one is a security decision with a single defensible answer,
/// and a deployment that could weaken it would eventually be a deployment that had.
/// </remarks>
internal static class McpOAuthAuthentication
{
    /// <summary>The scheme that decides which credential a request presented and forwards it to the handler that judges it.</summary>
    internal const string RoutingSchemeName = "MailMcpTransport";

    /// <summary>The signature algorithms a token may be signed with.</summary>
    /// <remarks>
    /// <para>
    /// An allow-list rather than a rejection list, so an algorithm nobody considered is refused rather than accepted.
    /// <c>none</c> is absent, which is what refuses an unsigned token, and every symmetric algorithm is absent too: a
    /// symmetric key is a shared secret, so accepting <c>HS256</c> would mean anything holding a copy of a verification
    /// key could mint tokens with it — including a public key mistaken for one.
    /// </para>
    /// <para>
    /// The list is fixed here rather than read from the discovery document. A server states which algorithms it can sign
    /// with, and that is a capability, not a policy; taking it as policy would let a server decide what MailMcp accepts.
    /// </para>
    /// </remarks>
    internal static readonly string[] PermittedSignatureAlgorithms =
    [
        SecurityAlgorithms.RsaSha256,
        SecurityAlgorithms.RsaSha384,
        SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.RsaSsaPssSha256,
        SecurityAlgorithms.RsaSsaPssSha384,
        SecurityAlgorithms.RsaSsaPssSha512,
        SecurityAlgorithms.EcdsaSha256,
        SecurityAlgorithms.EcdsaSha384,
        SecurityAlgorithms.EcdsaSha512,
    ];

    /// <summary>The media types a token's <c>typ</c> header may carry.</summary>
    /// <remarks>
    /// RFC 9068 defines <c>at+jwt</c> for an access token, but servers in wide deployment emit <c>JWT</c> and
    /// <c>Bearer</c> instead, so requiring the standard type alone would refuse most real tokens. The list is therefore
    /// what excludes an exotic type rather than what separates an access token from an identity token — an identity token
    /// is typed <c>JWT</c> as well, and what refuses one here is that its audience is the client it was issued to rather
    /// than this resource.
    /// </remarks>
    internal static readonly string[] PermittedTokenTypes = ["at+jwt", "application/at+jwt", "JWT", "Bearer"];

    /// <summary>How much disagreement between this host's clock and the authorization server's is tolerated.</summary>
    /// <remarks>Shorter than the framework's five minutes, because access tokens are short-lived and both machines are expected to keep time; a wider window would extend the life of every expired token by the same amount.</remarks>
    internal static readonly TimeSpan PermittedClockSkew = TimeSpan.FromSeconds(60);

    /// <summary>How often the discovery document and key set are re-read even when nothing has failed.</summary>
    internal static readonly TimeSpan MetadataRefreshInterval = TimeSpan.FromHours(1);

    /// <summary>The shortest time between two refreshes a failed validation can provoke.</summary>
    /// <remarks>
    /// A token naming a key identifier the host has never seen asks for an immediate refresh, which is how a rotated
    /// signing key is picked up within seconds rather than at the next scheduled read. It is also how an unauthenticated
    /// caller could make the host fetch from the authorization server as fast as it can send requests, so the request is
    /// throttled to this interval: a flood of unknown key identifiers costs one retrieval per interval, and a genuine
    /// rotation is still noticed inside it. Stated here rather than left to the framework's own default, so the throttle
    /// is a decision this repository made.
    /// </remarks>
    internal static readonly TimeSpan MetadataRefreshThrottle = TimeSpan.FromSeconds(30);

    /// <summary>How long a previously valid discovery document and key set stay usable after the server stops answering.</summary>
    /// <remarks>
    /// An authorization server that becomes unreachable must not immediately refuse every request, and must not keep
    /// signing keys trusted forever either. Within this window the last metadata that was known good still validates
    /// tokens; past it, authentication fails closed and the deployment is refusing requests it cannot verify.
    /// </remarks>
    internal static readonly TimeSpan LastKnownGoodMetadataLifetime = TimeSpan.FromHours(1);

    /// <summary>Names the scheme that validates tokens from one configured authorization server.</summary>
    /// <param name="authorizationServerName">The operator's name for the profile.</param>
    /// <returns>The scheme name.</returns>
    internal static string SchemeNameFor(string authorizationServerName) =>
        $"MailMcpOAuth:{authorizationServerName}";

    /// <summary>States what a token from one authorization server must satisfy to be accepted.</summary>
    /// <param name="issuer">The profile's issuer, compared against the token's <c>iss</c>.</param>
    /// <param name="canonicalResource">The resource identifier the token's audience must name.</param>
    /// <returns>The validation rules for that profile.</returns>
    /// <remarks>Composed here rather than inline where the scheme is registered, because these are the deployment's acceptance rules and are worth reading, and asserting, as one thing.</remarks>
    internal static TokenValidationParameters TokenValidationParametersFor(string issuer, string canonicalResource) =>
        new()
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,

            // The audience is the canonical resource, which is what refuses a token issued for some other service on the
            // same authorization server. A valid signature from a trusted issuer is not by itself a reason to serve
            // anyone's mailbox, and neither is a token that merely carries the right scope.
            ValidateAudience = true,
            ValidAudience = canonicalResource,

            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = PermittedClockSkew,

            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = PermittedSignatureAlgorithms,
            ValidTypes = PermittedTokenTypes,

            // Keeps a previously retrieved key set usable for a bounded window when the authorization server stops
            // answering, rather than refusing every request the moment it becomes unreachable or trusting it forever.
            ValidateWithLKG = true,

            NameClaimType = McpOAuthIdentity.SubjectClaimType,

            // A claim type nothing issues, which is what makes a role check answer no. The validator rejects an empty
            // one, and the framework's default would let a 'role' claim an authorization server chose to include answer
            // a check no configuration ever authorized.
            RoleClaimType = McpOAuthIdentity.RoleClaimType,
        };
}

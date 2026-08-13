// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Common.OAuth;

namespace MailFathom.Host.Configuration.Access;

/// <summary>What this deployment is called in OAuth terms, and which authorization servers may speak for it.</summary>
/// <remarks>
/// <para>
/// MailFathom is a resource server and never an authorization server. It stores no password, issues no token, redeems no
/// authorization code, and holds no refresh token. What it owns is the other half of the arrangement: a name it is known
/// by, a list of servers whose signatures it trusts, and the scopes a token must carry to reach a mailbox.
/// </para>
/// <para>
/// <see cref="Resource" /> and <see cref="RequiredScopes" /> are stated once for the deployment rather than per
/// authorization server, and that is the point. A token is accepted because it was issued for <em>this</em> resource
/// with <em>these</em> scopes, so making either of them a per-profile setting would let one server be trusted on easier
/// terms than another, and the boundary would then be as weak as its weakest profile. What a profile carries is where
/// its keys come from; what a token must prove is the same whichever profile signed it.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class OAuthValidationOptions
{
    /// <summary>How long the host waits for a discovery document or a key set, after which the retrieval fails rather than holding a request.</summary>
    /// <remarks>A constant rather than a setting. It bounds an outbound call an operator never sees, and a deployment that needed it longer would be reporting a problem with the authorization server rather than with this value.</remarks>
    internal static readonly TimeSpan MetadataRetrievalTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long a pooled connection to an authorization server is reused, after which the next retrieval resolves the address again.</summary>
    /// <remarks>
    /// The backchannel is held for the life of the scheme that owns it, so nothing else would ever make it look the
    /// address up a second time, and an authorization server that moves would keep being reached at where it used to be
    /// until the process restarted. Five minutes is short enough that a move is followed within one refresh interval and
    /// long enough that a key set fetched every few hours costs no handshake worth counting.
    /// </remarks>
    internal static readonly TimeSpan MetadataConnectionLifetime = TimeSpan.FromMinutes(5);

    /// <summary>The largest discovery document or key set the host reads, beyond which the retrieval fails.</summary>
    /// <remarks>A real document is a few kilobytes. The limit exists so a server that has been replaced, misconfigured, or compromised cannot make the host buffer an unbounded response during a key refresh.</remarks>
    internal const int MetadataSizeLimitInBytes = 256 * 1024;

    /// <summary>Gets or sets the canonical URL clients name when they ask for a token to use here, for example <c>https://mail.example.test/mcp</c>.</summary>
    /// <remarks>
    /// <para>
    /// This is the resource identifier of RFC 8707, which a client sends as the <c>resource</c> parameter and an
    /// authorization server puts in the token's audience. Every token is checked against it, which is what stops a token
    /// issued for some other service on the same authorization server from being replayed here.
    /// </para>
    /// <para>
    /// It is the address clients actually reach, not an internal one: it is published in the protected resource metadata
    /// document and a client will use it verbatim. A deployment behind a reverse proxy therefore writes the proxy's
    /// public URL here rather than the address the host binds.
    /// </para>
    /// </remarks>
    public string? Resource { get; set; }

    /// <summary>Gets the scopes a token must carry before any tool runs.</summary>
    /// <remarks>
    /// Published in the protected resource metadata alongside <see cref="AdvertisedScopes" /> and named in the challenge
    /// that answers a token lacking them, so a client learns what to ask for from the refusal itself. Leaving it empty
    /// accepts any token this resource's authorization servers issued, which is a coarser boundary rather than a broken
    /// one; it is the right setting only where the authorization server already restricts who receives a token for this
    /// resource.
    /// </remarks>
    public IList<string> RequiredScopes { get; } = [];

    /// <summary>Gets the scopes published for a client to ask for on top of the required ones, and checked on no token.</summary>
    /// <remarks>
    /// <para>
    /// What a client should ask for and what a token is refused for lacking are two lists, and RFC 9728's
    /// <c>scopes_supported</c> is the first of them. <c>offline_access</c> is what makes the difference visible: a client
    /// that never asks for it is issued no refresh token and sends its user back through the authorization server every
    /// time the access token expires, while requiring it here would refuse every token from a server that leaves it out
    /// of the <c>scope</c> claim — which such a server may do, because the value describes the client's own session
    /// rather than anything this resource protects.
    /// </para>
    /// <para>
    /// Published beside <see cref="RequiredScopes" /> rather than instead of it: a required scope is advertised whether
    /// it appears here or not, and a scope named here is never enforced. A value repeating a required one is refused
    /// rather than folded away, so this list goes on saying exactly what the required list does not.
    /// </para>
    /// </remarks>
    public IList<string> AdvertisedScopes { get; } = [];

    /// <summary>Gets the external authorization servers whose access tokens are accepted.</summary>
    public IList<AuthorizationServerOptions> AuthorizationServers { get; } = [];

    /// <summary>Gets whether anything at all was configured in this section.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(this.Resource)
        || this.RequiredScopes.Count > 0
        || this.AdvertisedScopes.Count > 0
        || this.AuthorizationServers.Any(authorizationServer => authorizationServer.IsConfigured);

    /// <summary>Finds everything an operator must fix before OAuth tokens can be validated.</summary>
    /// <returns>One message per faulty setting, relative to this section, empty when the settings are usable.</returns>
    public IReadOnlyList<string> FindConfigurationErrors()
    {
        var errors = new List<string>();

        if (!OAuthIdentifierUri.TryCanonicalize(this.Resource, out _))
        {
            errors.Add($"{nameof(this.Resource)} — the configured value is not a canonical resource URL; write the absolute https URL clients reach this endpoint at, with no user information, no query, and no fragment.");
        }

        if (this.AuthorizationServers.Count == 0)
        {
            errors.Add($"{nameof(this.AuthorizationServers)} — OAuth authentication is selected and no authorization server is configured, so no token could be validated.");
        }

        errors.AddRange(this.FindRequiredScopeErrors());
        errors.AddRange(this.FindAdvertisedScopeErrors());
        errors.AddRange(this.FindAuthorizationServerErrors());

        return errors;
    }

    /// <summary>Reports the identifier every token's audience is compared against.</summary>
    /// <returns>The canonical resource identifier.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the settings have not passed <see cref="FindConfigurationErrors" />.</exception>
    public string CanonicalResource() =>
        OAuthIdentifierUri.TryCanonicalize(this.Resource, out var canonicalResource)
            ? canonicalResource
            : throw new InvalidOperationException(
                "The canonical resource was read before it was validated, so it is not usable as a resource identifier.");

    /// <summary>Reports the identities a token must name to be served, across every configured authorization server.</summary>
    /// <returns>The issuer and subject pairs, compared exactly.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the settings have not passed <see cref="FindConfigurationErrors" />.</exception>
    public HashSet<string> AuthorizedIdentities() =>
        [.. this.AuthorizationServers.SelectMany(authorizationServer => authorizationServer.AuthorizedIdentities())];

    private IEnumerable<string> FindRequiredScopeErrors()
    {
        var claimedScopes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (index, configuredScope) in this.RequiredScopes.Index())
        {
            var settingPath = $"{nameof(this.RequiredScopes)}:{index}";

            // A scope reaches a client through the space-separated 'scope' parameter of a WWW-Authenticate challenge, so
            // one carrying a space, a quotation mark, or a backslash would either split into two scopes or end the
            // header parameter early. RFC 6749 section 3.3 already excludes exactly those characters from a scope token.
            if (!IsScopeToken(configuredScope))
            {
                yield return $"{settingPath} — '{configuredScope}' is not a scope; write the value the authorization server issues, with no space, quotation mark, or backslash in it.";
            }
            else if (!claimedScopes.Add(configuredScope))
            {
                yield return $"{settingPath} — '{configuredScope}' repeats a scope the list already carries.";
            }
        }
    }

    /// <summary>Finds what an operator must fix in the scopes this entry advertises without checking.</summary>
    /// <remarks>
    /// A malformed value is refused exactly as a malformed required one is, and for the metadata document alone: a
    /// client composes the space-separated <c>scope</c> parameter of its own authorization request out of what it reads
    /// there, so a space or a quotation mark splits one scope into two. No challenge carries one — only a required scope
    /// reaches a <c>WWW-Authenticate</c> header, because that is the only kind a token is turned away for lacking. A
    /// value that is already required is refused as well: every required scope is published regardless, so repeating one
    /// here states nothing and would leave the list reading as the whole advertised set rather than as what is
    /// advertised beyond what is checked.
    /// </remarks>
    private IEnumerable<string> FindAdvertisedScopeErrors()
    {
        var requiredScopes = new HashSet<string>(this.RequiredScopes, StringComparer.Ordinal);
        var claimedScopes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (index, configuredScope) in this.AdvertisedScopes.Index())
        {
            var settingPath = $"{nameof(this.AdvertisedScopes)}:{index}";

            if (!IsScopeToken(configuredScope))
            {
                yield return $"{settingPath} — '{configuredScope}' is not a scope; write the value the authorization server issues, with no space, quotation mark, or backslash in it.";
            }
            else if (!claimedScopes.Add(configuredScope))
            {
                yield return $"{settingPath} — '{configuredScope}' repeats a scope the list already carries.";
            }
            else if (requiredScopes.Contains(configuredScope))
            {
                yield return $"{settingPath} — '{configuredScope}' is already required and is published for that reason; list here only a scope this endpoint advertises without checking it.";
            }
        }
    }

    private IEnumerable<string> FindAuthorizationServerErrors()
    {
        var claimedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var claimedIssuers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (index, authorizationServer) in this.AuthorizationServers.Index())
        {
            var settingPath = $"{nameof(this.AuthorizationServers)}:{index}";

            var profileErrors = authorizationServer.FindConfigurationErrors();
            if (profileErrors.Count > 0)
            {
                foreach (var profileError in profileErrors)
                {
                    yield return $"{settingPath}:{profileError}";
                }

                continue;
            }

            if (!claimedNames.Add(authorizationServer.Name!))
            {
                yield return $"{settingPath}:{nameof(AuthorizationServerOptions.Name)} — '{authorizationServer.Name}' repeats a name another authorization server already carries.";
            }

            // Two profiles claiming one issuer is the ambiguity the whole selection rule exists to avoid: a token naming
            // that issuer would be validated against whichever profile happened to be registered first, so the key set a
            // token is trusted against would depend on configuration order rather than on what the token says.
            if (!claimedIssuers.Add(authorizationServer.ValidatedIssuer()))
            {
                yield return $"{settingPath}:{nameof(AuthorizationServerOptions.Issuer)} — this issuer repeats one another authorization server already carries, which would leave the key set a token is trusted against decided by configuration order.";
            }
        }
    }

    private static bool IsScopeToken(string? configuredScope) =>
        !string.IsNullOrEmpty(configuredScope)
        && configuredScope.All(character => character is > (char)0x20 and < (char)0x7F and not '"' and not '\\');
}

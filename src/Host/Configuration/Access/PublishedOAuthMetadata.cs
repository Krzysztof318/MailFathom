// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.Access;

/// <summary>What a protected surface's configured OAuth entries publish about themselves to a client holding nothing yet.</summary>
/// <param name="Resource">The identifier a token must be issued for, which every entry names identically.</param>
/// <param name="AuthorizationServers">Every issuer a token may come from, across every entry.</param>
/// <param name="ScopesSupported">Every scope a client should ask for, listed once however many entries name it.</param>
/// <remarks>
/// <para>
/// Two surfaces publish an RFC 9728 document and neither may publish a different one: the MCP endpoint through the
/// protocol SDK's own type, the administrative endpoint through a record of this repository's own so that signing in to
/// a deployment does not depend on the MCP protocol library. Those are two serialization shapes for one decision, and
/// this is the decision — which resource, which issuers, which scopes — computed once so the two cannot drift into
/// publishing different answers from the same configuration.
/// </para>
/// <para>
/// One document however many entries are configured, because it describes one protected resource and is published at an
/// address derived from that resource's identifier. Every entry names the same resource, which configuration validation
/// is what guarantees, so the resource comes from the first; the two lists carry what all of them accept, because a
/// client reads this to find out where to authorize and what to ask for.
/// </para>
/// <para>
/// <see cref="ScopesSupported" /> is what a client should ask for rather than what a token is checked against, which is
/// what RFC 9728 defines the field as. It therefore composes an entry's required scopes together with the ones it
/// advertises without checking — <c>offline_access</c> being the case that needs the distinction, since a client has to
/// ask for it to be issued a refresh token while a token proves nothing by carrying it. Enforcement reads
/// <see cref="OAuthValidationOptions.RequiredScopes" /> directly and never this list, so advertising a scope can widen
/// what a client requests and can never narrow who is served.
/// </para>
/// <para>
/// A permission joins that list from every entry whose grant a token's own scopes narrow, and from no other, which
/// follows from the same reading of the field: a permission the deployment grants from configuration is not something
/// any client can ask for. The union of those entries' ceilings is therefore also exactly what an operator has to
/// create as scopes in their authorization server, read from the document rather than transcribed out of their own
/// configuration file.
/// </para>
/// </remarks>
internal sealed record PublishedOAuthMetadata(
    string Resource,
    IReadOnlyList<string> AuthorizationServers,
    IReadOnlyList<string> ScopesSupported)
{
    /// <summary>Composes what the configured entries publish between them.</summary>
    /// <param name="methods">The configured credential entries, in configuration order.</param>
    /// <param name="surface">The surface these entries guard, which decides what an entry that wrote no grant advertises.</param>
    /// <returns>The published metadata.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="methods" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when no entry states OAuth, which is a surface accepting no token at all.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the settings have not passed their configuration errors.</exception>
    /// <remarks>
    /// The whole entry rather than its OAuth block, because the grant belongs to the entry and the document has to
    /// carry it. Reading the blocks alone would leave the two halves of one entry consulted in two places, which is how
    /// a document comes to advertise a ceiling the entry beside it never granted.
    /// </remarks>
    internal static PublishedOAuthMetadata For(
        IReadOnlyList<TransportAuthenticationOptions> methods,
        ProtectedSurface surface)
    {
        ArgumentNullException.ThrowIfNull(methods);

        var oauthMethods = TransportAuthenticationConfiguration.OAuthMethodsIn(methods);

        if (oauthMethods.Count == 0)
        {
            throw new ArgumentException(
                "A protected resource metadata document describes the configured OAuth methods, and none was configured.",
                nameof(methods));
        }

        var advertisedPermissions = methods
            .Where(method => method.PermissionsFromTokenScopes && method.OAuth is not null)
            .SelectMany(method => method.GrantedPermissions(surface))
            .Select(permission => permission.Name);

        return new PublishedOAuthMetadata(
            oauthMethods[0].CanonicalResource(),
            [
                .. oauthMethods
                    .SelectMany(oauthMethod => oauthMethod.AuthorizationServers)
                    .Select(authorizationServer => authorizationServer.ValidatedIssuer()),
            ],
            [
                .. oauthMethods
                    .SelectMany(oauthMethod => oauthMethod.RequiredScopes)
                    .Concat(oauthMethods.SelectMany(oauthMethod => oauthMethod.AdvertisedScopes))
                    .Concat(advertisedPermissions)
                    .Distinct(StringComparer.Ordinal),
            ]);
    }
}

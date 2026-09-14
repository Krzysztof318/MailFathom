// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.Access;

/// <summary>What a mail-serving endpoint offers a browser that is about to draw a sign-in screen.</summary>
/// <param name="AcceptsPassword">Whether a user name and a password may be presented here at all.</param>
/// <param name="AuthorizationServers">One entry per authorization server a person may sign in through, in configuration order.</param>
/// <remarks>
/// <para>
/// The RFC 9728 document beside this one already tells a client which issuers are trusted, which resource a token must
/// be issued for, and which scopes to ask for — everything a client that already knows how to authorize needs. What it
/// cannot carry is what a <em>screen</em> needs: whether a password form belongs on it, what to write on a button, and
/// the client identifier an authorization request starts with. This is that, and nothing that document already
/// publishes is published twice.
/// </para>
/// <para>
/// Composed once and read twice, which is why it is a type rather than a shape assembled inside the route. The route
/// answers it to a browser, and the content security policy the same browser's page is served under has to admit each
/// published issuer's origin in <c>connect-src</c> — two answers that would otherwise be derived from one configuration
/// in two places and could come to disagree about which servers a person is offered.
/// </para>
/// </remarks>
internal sealed record PublishedSignInMethods(
    bool AcceptsPassword,
    IReadOnlyList<PublishedSignInMethod> AuthorizationServers)
{
    /// <summary>Composes what one endpoint's configured entries offer a browser.</summary>
    /// <param name="methods">The endpoint's configured credential entries, in configuration order.</param>
    /// <returns>The published methods.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="methods" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the settings have not passed their configuration errors.</exception>
    /// <remarks>
    /// An endpoint accepting no credential at all answers <see cref="AcceptsPassword" /> as <see langword="true" />,
    /// which is not a special case so much as the honest answer: it will take a request carrying a password exactly as
    /// it takes one carrying nothing, and asking somebody to notice the difference would be asking them about a
    /// configuration that is not theirs. It is the same reading the client's own first request already reports for that
    /// deployment.
    /// </remarks>
    internal static PublishedSignInMethods For(IReadOnlyList<UserFacingAuthenticationOptions> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);

        var acceptsPassword =
            methods.Count == 0
            || UserFacingAuthenticationConfiguration.Accepts(methods, UserCredentialMethod.Password);

        var offered = UserFacingAuthenticationConfiguration.OAuthMethodsIn(methods)
            .SelectMany(oauth => oauth.AuthorizationServers)
            .Where(server => server.IsOfferedToABrowser)
            .Select(server => new PublishedSignInMethod(
                server.ValidatedIssuer(),
                server.PublishedName(),
                server.PublishedDisplayName(),
                server.PublishedClientId()));

        return new PublishedSignInMethods(acceptsPassword, [.. offered]);
    }

    /// <summary>Reports the origins a page has to be permitted to call to complete any of these sign-ins.</summary>
    /// <returns>Each published issuer's scheme, host, and port, once however many servers share one.</returns>
    /// <remarks>
    /// The origin rather than the issuer, because that is what a content security policy source expression is: a client
    /// reaches an authorization server's discovery document and its token endpoint, and neither path is the issuer's
    /// own. Two profiles on one server — two realms, two tenants — therefore widen the policy once rather than twice.
    /// </remarks>
    internal IReadOnlyList<string> IssuerOrigins() =>
    [
        .. this.AuthorizationServers
            .Select(server => new Uri(server.Issuer).GetLeftPart(UriPartial.Authority))
            .Distinct(StringComparer.OrdinalIgnoreCase),
    ];
}

/// <summary>One authorization server a person may sign in to this deployment through.</summary>
/// <param name="Issuer">The issuer identifier, which the client reads the server's own discovery document from and asks for a token against.</param>
/// <param name="Name">What the deployment calls this server, which is <c>self</c> for its own identity provider and otherwise the name a client matches a provider mark against.</param>
/// <param name="DisplayName">The words the control carries, which is <paramref name="Name" /> unless the operator wrote something else.</param>
/// <param name="ClientId">The identifier the authorization request is started with, registered by the operator at that server.</param>
/// <remarks>
/// Nothing in it is a secret. An issuer is a deployment's own public name for somebody else's server, a client
/// identifier travels in the address bar of every authorization request the flow makes, and the two names are words on
/// a button — which is what lets the whole document answer a caller holding nothing, as it must, since its reader has
/// nothing to authenticate with yet.
/// </remarks>
internal sealed record PublishedSignInMethod(string Issuer, string Name, string DisplayName, string ClientId);

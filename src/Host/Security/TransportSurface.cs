// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Security;

/// <summary>One separately protected transport surface, and the names the authentication framework knows it by.</summary>
/// <remarks>
/// <para>
/// A surface is a set of routes with one credential policy in front of it. The MCP endpoint is one, and it is the reason
/// this type exists: everything that decides whether a request is authenticated used to name that endpoint in a
/// constant, so a second surface could not have been protected without a second copy of the same code. Passing the
/// surface instead makes the schemes, the policy, and the keys parameters of a registration rather than properties of
/// the process.
/// </para>
/// <para>
/// The names below are what keeps two surfaces from reaching each other. Each registers its own routing scheme, and each
/// endpoint requires an authorization policy naming only that scheme, so a credential the other surface accepts is never
/// consulted for this one. That isolation is by scheme name rather than by an explicit check, which is what makes it
/// hold for every route on the surface instead of for the routes somebody remembered.
/// </para>
/// <para>
/// All four names are internal handles: nothing published, persisted, or configured reads them. A challenge names
/// <see cref="ApiKeyAuthentication.Realm" /> rather than a scheme, and a client is never told which scheme judged it.
/// They are derived from <see cref="Name" /> rather than stated per surface so that adding one cannot accidentally
/// reuse another's, which would silently merge the two policies.
/// </para>
/// <para>
/// It is a closed enumeration for the reason <see cref="Hosting.HealthProbe" /> is one: a surface is a decision about
/// what the host serves, not a value a caller constructs. Being a struct, <see langword="default" /> is reachable and is
/// not a surface; it reports itself through <see cref="IsSpecified" /> and refuses to answer for a name.
/// </para>
/// </remarks>
internal readonly record struct TransportSurface
{
    private readonly string? name;

    private TransportSurface(string name) => this.name = name;

    /// <summary>Gets the surface serving the MCP protocol.</summary>
    internal static TransportSurface Mcp { get; } = new("Mcp");

    /// <summary>Gets whether this value names a surface rather than the unusable struct default.</summary>
    internal bool IsSpecified => this.name is not null;

    /// <summary>Gets the surface's name, which every scheme and policy name below is composed from.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a surface.</exception>
    internal string Name => this.name
        ?? throw new InvalidOperationException("The value is the default of the struct and names no transport surface.");

    /// <summary>Gets the scheme that decides which credential a request presented and forwards it to the handler that judges it.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a surface.</exception>
    internal string RoutingSchemeName => $"MailFathom:{this.Name}:Transport";

    /// <summary>Gets the scheme comparing a presented credential against this surface's configured API keys.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a surface.</exception>
    internal string ApiKeySchemeName => $"MailFathom:{this.Name}:ApiKey";

    /// <summary>Gets the name this surface's authorization requirement is registered under.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a surface.</exception>
    internal string AccessPolicyName => $"MailFathom:{this.Name}:Access";

    /// <summary>Names the scheme that validates tokens from one of this surface's configured authorization servers.</summary>
    /// <param name="authorizationServerName">The operator's name for the profile.</param>
    /// <returns>The scheme name.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="authorizationServerName" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a surface.</exception>
    /// <remarks>
    /// The surface's name precedes the server's, so two surfaces configuring one authorization server under the same
    /// operator-chosen name still register two schemes with two key sets. Sharing one would let either surface's
    /// configuration decide what the other trusts.
    /// </remarks>
    internal string OAuthSchemeNameFor(string authorizationServerName)
    {
        ArgumentNullException.ThrowIfNull(authorizationServerName);

        return $"MailFathom:{this.Name}:OAuth:{authorizationServerName}";
    }

    /// <inheritdoc />
    /// <remarks>The name, because that is what a diagnostic about a surface is read by.</remarks>
    public override string ToString() => this.name ?? "(unspecified)";
}

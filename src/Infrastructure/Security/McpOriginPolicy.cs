// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Security;

/// <summary>Which browser origins the MCP endpoint answers.</summary>
/// <remarks>
/// <para>
/// The MCP Streamable HTTP transport requires a server to validate the <c>Origin</c> header, because a page the user
/// never visited can otherwise make a browser send authenticated requests to an address it resolved back to the
/// operator's own host. That check is what this type owns, and it is deliberately separate from the CORS response
/// headers: CORS tells a browser what it may read, whereas this decides whether the request is served at all.
/// </para>
/// <para>
/// It is not authentication and must never be mistaken for it. A non-browser client sends no <c>Origin</c> at all and
/// is served exactly as before, and any client that chooses its own headers can send whichever origin it likes. The
/// value of the check is confined to the one attacker it is aimed at: a browser, which sets the header itself and does
/// not let a page forge it.
/// </para>
/// </remarks>
public sealed class McpOriginPolicy
{
    private readonly HashSet<string> allowedOrigins;

    private McpOriginPolicy(bool allowsAnyOrigin, IEnumerable<string> allowedOrigins)
    {
        this.AllowsAnyOrigin = allowsAnyOrigin;
        this.allowedOrigins = new HashSet<string>(allowedOrigins, StringComparer.Ordinal);
    }

    /// <summary>Gets the policy that serves every origin, which is what a deployment configuring none receives.</summary>
    public static McpOriginPolicy AllowingAnyOrigin { get; } = new(allowsAnyOrigin: true, []);

    /// <summary>Gets the policy that serves no browser at all, which a deployment states by configuring an empty origin list.</summary>
    /// <remarks>
    /// It refuses every request carrying an <c>Origin</c> and serves every request carrying none, so what it excludes is
    /// browsers rather than clients. That is the accurate posture for a deployment whose only consumers are agents and
    /// command-line clients, and it is the one posture a list of origins cannot express.
    /// </remarks>
    public static McpOriginPolicy ServingNoBrowserOrigin { get; } = new(allowsAnyOrigin: false, []);

    /// <summary>Gets whether every origin is served.</summary>
    public bool AllowsAnyOrigin { get; }

    /// <summary>Gets the origins this policy serves, in their normalized form, empty when every origin is served.</summary>
    public IReadOnlyCollection<string> AllowedOrigins => this.allowedOrigins;

    /// <summary>Creates a policy that serves an exact set of origins.</summary>
    /// <param name="allowedOrigins">The configured origins, which must already have passed <see cref="TryNormalize" />.</param>
    /// <returns>The restricting policy.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="allowedOrigins" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="allowedOrigins" /> is empty; serving no browser at all is <see cref="ServingNoBrowserOrigin" />, which says so by its name rather than by a list that happens to be short.</exception>
    public static McpOriginPolicy Restricting(IEnumerable<string> allowedOrigins)
    {
        ArgumentNullException.ThrowIfNull(allowedOrigins);

        var origins = allowedOrigins.ToArray();

        return origins.Length > 0
            ? new McpOriginPolicy(allowsAnyOrigin: false, origins)
            : throw new ArgumentException(
                "A restricting origin policy must name at least one origin.",
                nameof(allowedOrigins));
    }

    /// <summary>Normalizes a configured origin into the form a browser sends.</summary>
    /// <param name="configuredValue">The configured origin, for example <c>https://client.example.test</c>.</param>
    /// <param name="normalizedOrigin">The serialized origin when the value is accepted; otherwise an empty string.</param>
    /// <returns><see langword="true" /> when the value is an origin this policy can compare against; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// A browser sends the serialized origin of RFC 6454: a lowercase scheme and host, and the port only when it is not
    /// the scheme's default. Configuration is normalized to that same form so a comparison is an equality test rather
    /// than a set of special cases, and so <c>https://Client.Example.Test:443/</c> and <c>https://client.example.test</c>
    /// cannot be two different entries in one list. A path, a query, a fragment, or user information means the operator
    /// wrote a URL where an origin belongs, and is refused rather than silently discarded.
    /// </remarks>
    public static bool TryNormalize(string? configuredValue, out string normalizedOrigin)
    {
        normalizedOrigin = string.Empty;

        if (string.IsNullOrWhiteSpace(configuredValue)
            || !Uri.TryCreate(configuredValue.Trim(), UriKind.Absolute, out var origin))
        {
            return false;
        }

        var carriesOnlyAnAuthority = origin.AbsolutePath is "/"
            && string.IsNullOrEmpty(origin.Query)
            && string.IsNullOrEmpty(origin.Fragment)
            && string.IsNullOrEmpty(origin.UserInfo);

        if (!carriesOnlyAnAuthority
            || (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        normalizedOrigin = origin.GetLeftPart(UriPartial.Authority);

        return true;
    }

    /// <summary>Gets whether a request carrying a given <c>Origin</c> is served.</summary>
    /// <param name="origin">The header value, or <see langword="null" /> when the request carried none.</param>
    /// <returns><see langword="true" /> when the request is served; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// A request with no <c>Origin</c> is served, because that is every non-browser client and the header is not a
    /// credential. An opaque origin, which a browser spells <c>null</c>, normalizes to nothing and is refused under a
    /// restricting policy: it names no origin an operator could have listed.
    /// </remarks>
    public bool Permits(string? origin)
    {
        if (string.IsNullOrEmpty(origin) || this.AllowsAnyOrigin)
        {
            return true;
        }

        return TryNormalize(origin, out var normalizedOrigin) && this.allowedOrigins.Contains(normalizedOrigin);
    }
}

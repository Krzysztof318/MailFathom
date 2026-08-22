// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Specialized;

namespace MailFathom.Client.Backend.Authorization.Redirect;

/// <summary>What the authorization server sent back to this application after the person answered it.</summary>
/// <param name="Code">The authorization code to redeem, absent from a refusal.</param>
/// <param name="State">The opaque value the request carried, which binds this answer to it.</param>
/// <param name="Error">The machine-readable refusal code, absent from an approval.</param>
/// <remarks>
/// A redirect is the one message in the flow that arrives through the person's browser rather than over a connection
/// this process opened, so nothing in it is trusted until the state has been compared. That comparison belongs to the
/// caller rather than to whichever head caught the redirect.
/// </remarks>
public sealed record SignInRedirect(string? Code, string? State, string? Error)
{
    /// <summary>Gets whether this carries an answer at all, rather than being a request that merely arrived.</summary>
    /// <remarks>
    /// A freshly bound redirect address answers whatever reaches it, and a browser prefetch, a port scan, or a stale
    /// tab reaches it without carrying any of the three. This says only that something was addressed to this flow;
    /// whether it belongs to <em>this</em> sign-in is the state comparison's answer and nothing else's.
    /// </remarks>
    internal bool CarriesAnAnswer => this.Code is not null || this.State is not null || this.Error is not null;

    /// <summary>Reads a redirect out of the query the browser came back with.</summary>
    /// <param name="query">The query part of the address the redirect landed on, with or without its leading question mark.</param>
    /// <returns>What the query carried, with every part absent where the query held nothing for it.</returns>
    /// <remarks>
    /// Parsed rather than assumed well formed: this is attacker-influenced text by construction, since anything that
    /// can navigate a browser can put a query in front of this. A missing part is read as absent rather than refused
    /// here, so one place — the caller comparing the state — decides what an unusable redirect means.
    /// </remarks>
    public static SignInRedirect FromQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new SignInRedirect(null, null, null);
        }

        var parameters = ParseQuery(query.TrimStart('?'));

        return new SignInRedirect(parameters["code"], parameters["state"], parameters["error"]);
    }

    /// <summary>Splits a query into its parameters, decoding each the way a form-encoded value is written.</summary>
    /// <remarks>
    /// Hand-rolled rather than taken from <c>System.Web</c>, which is not part of a net10.0 library's framework, and
    /// deliberately not from a routing package: three parameters read once do not earn a dependency in an assembly
    /// whose whole purpose is to stay small enough that its reference graph is the boundary.
    /// </remarks>
    private static NameValueCollection ParseQuery(string query)
    {
        var parameters = new NameValueCollection(StringComparer.Ordinal);

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);

            if (separator <= 0)
            {
                continue;
            }

            parameters[Uri.UnescapeDataString(pair[..separator].Replace('+', ' '))] =
                Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
        }

        return parameters;
    }
}

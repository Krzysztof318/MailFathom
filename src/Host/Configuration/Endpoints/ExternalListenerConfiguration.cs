// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>Refuses the host's own ways of naming a listener, which no longer decide anything here.</summary>
/// <remarks>
/// <para>
/// Kestrel takes listeners from three places and the three do not compose: addresses shaped as URLs
/// (<c>ASPNETCORE_URLS</c>, <c>ASPNETCORE_HTTP_PORTS</c>, <c>ASPNETCORE_HTTPS_PORTS</c>, <c>--urls</c>), endpoints named
/// under <c>Kestrel:Endpoints</c>, and listeners bound in code. Every MailFathom surface now binds in code from its own
/// section, so a URL-shaped address is ignored the moment any of them is enabled and a configured Kestrel endpoint
/// would open a fourth socket beside them, serving whichever routes match on a listener no section describes.
/// </para>
/// <para>
/// Both are therefore refused rather than ignored. The alternative is the failure this exists to prevent: an operator
/// states a port, the process starts, and the surface answers somewhere else — no error, no warning an orchestrator
/// reads, just an address nothing is listening on. The message names the setting that replaces it, so the fix is read
/// rather than searched for.
/// </para>
/// <para>
/// This is also what keeps a decision in one place. Where a surface is served is its own section's question, and a
/// second mechanism that could answer it would make the answer depend on which of the two a reader happened to find.
/// </para>
/// </remarks>
internal static class ExternalListenerConfiguration
{
    /// <summary>The configuration section whose children each bind a listener of their own.</summary>
    internal const string KestrelEndpointsSectionName = "Kestrel:Endpoints";

    private const string UrlKey = "Url";

    /// <summary>The URL-shaped address keys, paired with the environment variable an operator wrote to reach each one.</summary>
    /// <remarks>
    /// The configuration keys are the host's own, and the variables are what an operator recognizes: a message naming
    /// <c>urls</c> would describe a key nobody typed. <c>--urls</c> and a launch profile's <c>applicationUrl</c> reach
    /// the same key and are reported against the same name, which is as close as this can come to naming what was
    /// actually written.
    /// </remarks>
    private static readonly (string ConfigurationKey, string Variable)[] UrlShapedAddressKeys =
    [
        (WebHostDefaults.ServerUrlsKey, "ASPNETCORE_URLS"),
        (WebHostDefaults.HttpPortsKey, "ASPNETCORE_HTTP_PORTS"),
        (WebHostDefaults.HttpsPortsKey, "ASPNETCORE_HTTPS_PORTS"),
    ];

    /// <summary>Finds every listener this deployment names outside the sections that own one.</summary>
    /// <param name="configuration">The application configuration, read at the root because the host's own keys are not nested under this product's section.</param>
    /// <returns>One message per setting, empty when the deployment names none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    internal static IReadOnlyList<string> FindConfigurationErrors(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var errors = new List<string>();

        errors.AddRange(UrlShapedAddressKeys
            .Where(key => !string.IsNullOrWhiteSpace(configuration[key.ConfigurationKey]))
            .Select(static key =>
                $"{key.Variable} — MailFathom serves no listener of its own from this variable. Each surface states where it is served in its own section: '{McpEndpointOptions.SectionName}:BindAddress' and '{McpEndpointOptions.SectionName}:Port' for the MCP endpoint, '{AdminEndpointOptions.SectionName}:*' for the administrative one, and '{HealthEndpointOptions.SectionName}:*' for the probes. Move the address there and remove this variable."));

        // A child without a URL binds nothing, which is what makes an endpoint carrying only defaults harmless.
        errors.AddRange(configuration.GetSection(KestrelEndpointsSectionName)
            .GetChildren()
            .Where(static endpoint => !string.IsNullOrWhiteSpace(endpoint[UrlKey]))
            .Select(static endpoint =>
                $"{KestrelEndpointsSectionName}:{endpoint.Key} — Kestrel binds a configured endpoint beside the listeners MailFathom opens, so this one would serve whichever routes matched on a socket no endpoint section describes and no section's credentials, rate limits, or isolation were composed for. State the address in the section of the surface it belongs to instead."));

        return errors;
    }
}

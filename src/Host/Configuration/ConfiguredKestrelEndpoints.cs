// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration;

/// <summary>Reads the listeners the host was configured with outside the MCP HTTPS profiles.</summary>
/// <remarks>
/// <para>
/// Kestrel takes listeners from two places and binds both: the ones code adds through <c>Listen</c>, and the ones the
/// <c>Kestrel:Endpoints</c> configuration section names. Binding in code replaces only the URL-shaped addresses — the
/// <c>ASPNETCORE_URLS</c> variable and anything that reaches <c>WebApplication.Urls</c> — so a configured endpoint keeps
/// its socket whatever else the composition root binds.
/// </para>
/// <para>
/// That is exactly the state <see cref="McpHttpsOptions" /> promises cannot exist: a clear-text listener serving the
/// same MCP route behind an endpoint an operator configured HTTPS for. Since the two cannot be reconciled — removing
/// the configured endpoint is a decision only an operator can take — the conflict fails startup and names both sides.
/// </para>
/// </remarks>
internal static class ConfiguredKestrelEndpoints
{
    /// <summary>The configuration section whose children each bind a listener of their own.</summary>
    internal const string SectionName = "Kestrel:Endpoints";

    private const string UrlKey = "Url";

    /// <summary>Reports whether the deployment names any Kestrel endpoint of its own.</summary>
    /// <param name="configuration">The application configuration, read at the root because the Kestrel section is not nested under this product's own.</param>
    /// <returns><see langword="true" /> when at least one configured endpoint binds a listener, otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A deployment that names its own endpoints is one whose URL-shaped addresses Kestrel is already ignoring, which is
    /// what <see cref="ConfiguredApplicationListeners" /> must not undo by restating them beside the endpoints an
    /// operator wrote.
    /// </remarks>
    internal static bool AnyConfigured(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetSection(SectionName)
            .GetChildren()
            .Any(static endpoint => !string.IsNullOrWhiteSpace(endpoint[UrlKey]));
    }

    /// <summary>Reads the TCP ports the configured endpoints bind.</summary>
    /// <param name="configuration">The application configuration, read at the root because the Kestrel section is not nested under this product's own.</param>
    /// <returns>The ports, without duplicates, empty when the deployment names no endpoint of its own.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// These are the application listener's ports whenever this section is populated, because the URL-shaped addresses
    /// are ignored as soon as it is. A second listener that has to avoid the application's therefore has to read them
    /// from here rather than from the addresses the deployment is no longer being bound from.
    /// </remarks>
    internal static IReadOnlyList<int> ListenerPorts(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return ConfiguredApplicationListeners.ListenerPorts(
        [
            .. configuration.GetSection(SectionName)
                .GetChildren()
                .Select(static endpoint => endpoint[UrlKey])
                .Where(static url => !string.IsNullOrWhiteSpace(url))
                .Select(static url => url!),
        ]);
    }

    /// <summary>Finds the configured Kestrel endpoints that would stay open behind the configured HTTPS profiles.</summary>
    /// <param name="configuration">The application configuration, read at the root because the Kestrel section is not nested under this product's own.</param>
    /// <param name="httpsSettings">The HTTPS profiles the MCP endpoint is served over.</param>
    /// <returns>One message per conflicting endpoint, empty when no HTTPS profile is configured or no endpoint conflicts with one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static IReadOnlyList<string> FindHttpsProfileConflicts(
        IConfiguration configuration,
        McpHttpsOptions httpsSettings)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(httpsSettings);

        if (!httpsSettings.TerminatesTls)
        {
            return [];
        }

        // A child without a URL binds nothing, which is what makes an endpoint that carries only defaults harmless.
        return
        [
            .. configuration.GetSection(SectionName)
                .GetChildren()
                .Where(static endpoint => !string.IsNullOrWhiteSpace(endpoint[UrlKey]))
                .Select(static endpoint =>
                    $"{SectionName}:{endpoint.Key} — a Kestrel endpoint is configured beside {McpEndpointOptions.SectionName}:{nameof(McpEndpointOptions.Https)}, and Kestrel binds both: this listener would stay open alongside the HTTPS profiles and serve the same MCP endpoint without the TLS they were configured to add. Remove the endpoint, or remove the HTTPS profiles and let this listener serve the endpoint."),
        ];
    }
}

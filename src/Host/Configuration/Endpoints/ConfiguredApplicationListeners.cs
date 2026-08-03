// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>Reads the listener the application itself is served on, and restates it where adding a second one would drop it.</summary>
/// <remarks>
/// <para>
/// Kestrel takes its listeners from three places and the three do not compose. A listener bound in code and an endpoint
/// named in <c>Kestrel:Endpoints</c> are both kept, but either one makes Kestrel ignore the URL-shaped addresses
/// entirely — <c>ASPNETCORE_URLS</c>, <c>ASPNETCORE_HTTP_PORTS</c>, and everything that reaches
/// <c>WebApplication.Urls</c> — and log that it is overriding them. That is by design where MailFathom binds the MCP
/// HTTPS profiles, because no clear-text listener may stay open behind them. It is a defect where the health-endpoint
/// listener is opened: a deployment that states its application port as <c>ASPNETCORE_HTTP_PORTS=8080</c> would keep
/// its probes and lose the port its clients connect to.
/// </para>
/// <para>
/// So the addresses are restated as <c>Kestrel:Endpoints</c> entries before the health listener is bound, which hands
/// the URL strings back to the framework's own parser rather than reimplementing it here: the same value binds the same
/// socket, under the same scheme, with the same certificate configuration it would have used. Only the two decisions
/// this file makes are its own — which configuration key supplies the addresses, and what "nothing configured" means.
/// </para>
/// <para>
/// Restating is confined to the case where the addresses were going to bind. A deployment that already names endpoints
/// in <c>Kestrel:Endpoints</c>, or one whose MCP HTTPS profiles bind in code, is one where the URL addresses were
/// already being ignored, and reinstating them there would open a listener the operator had removed.
/// </para>
/// </remarks>
internal static class ConfiguredApplicationListeners
{
    /// <summary>The endpoint name the restated addresses are written under, suffixed by their position.</summary>
    /// <remarks>Named after the product so it cannot collide with an endpoint an operator wrote, and readable in the "Now listening on" line it produces.</remarks>
    internal const string EndpointNamePrefix = "MailFathomApplication";

    /// <summary>The address the process serves when a deployment configures none.</summary>
    /// <remarks>
    /// Kestrel's own fallback is this address plus <c>https://localhost:5001</c> when an ASP.NET Core development
    /// certificate happens to be installed. The clear-text half is restated and the HTTPS half deliberately is not:
    /// MailFathom never serves a listener out of a development certificate, and a fallback that did would make the
    /// schemes a process listens on depend on what is installed on the machine rather than on what was configured.
    /// </remarks>
    private const string DefaultUrl = "http://localhost:5000";

    private const char UrlSeparator = ';';

    private const string UrlKey = "Url";

    /// <summary>Reads the URL-shaped addresses the application listener would be bound from.</summary>
    /// <param name="configuration">The application configuration, read at the root because the host's own keys are not nested under this product's section.</param>
    /// <returns>The addresses, in configuration order, never empty.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The precedence is the host's own: an explicit URL list wins over the port lists, and the port lists expand to
    /// every interface, which is what <c>ASPNETCORE_HTTP_PORTS</c> means. Reproducing it here rather than reading the
    /// result is unavoidable, because the host resolves it while starting and the listener has to be bound before that.
    /// </remarks>
    internal static IReadOnlyList<string> ResolveUrls(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configuredUrls = Split(configuration[WebHostDefaults.ServerUrlsKey]);

        if (configuredUrls.Count > 0)
        {
            return configuredUrls;
        }

        string[] portUrls =
        [
            .. ExpandPorts(configuration[WebHostDefaults.HttpPortsKey], Uri.UriSchemeHttp),
            .. ExpandPorts(configuration[WebHostDefaults.HttpsPortsKey], Uri.UriSchemeHttps),
        ];

        return portUrls.Length > 0 ? portUrls : [DefaultUrl];
    }

    /// <summary>Writes the addresses as the configuration section Kestrel binds its endpoints from.</summary>
    /// <param name="urls">The addresses the application listener serves.</param>
    /// <returns>One <c>Kestrel:Endpoints</c> entry per address.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="urls" /> is <see langword="null" />.</exception>
    internal static IReadOnlyList<KeyValuePair<string, string?>> AsKestrelEndpointConfiguration(IReadOnlyList<string> urls)
    {
        ArgumentNullException.ThrowIfNull(urls);

        return
        [
            .. urls.Index().Select(static entry => KeyValuePair.Create<string, string?>(
                $"{ConfiguredKestrelEndpoints.SectionName}:{EndpointNamePrefix}{entry.Index}:{UrlKey}",
                entry.Item)),
        ];
    }

    /// <summary>Reads the TCP ports the addresses bind, which is what a second listener must not collide with.</summary>
    /// <param name="urls">The addresses the application listener serves.</param>
    /// <returns>The ports, without duplicates.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="urls" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// An address the parser does not recognize contributes no port rather than a failure. Kestrel parses the same
    /// value moments later and reports it against the key an operator wrote; producing a second message here would
    /// describe the same mistake in this product's words and hide which setting the framework actually refused.
    /// </remarks>
    internal static IReadOnlyList<int> ListenerPorts(IReadOnlyList<string> urls)
    {
        ArgumentNullException.ThrowIfNull(urls);

        return [.. urls.Select(ParsePort).Where(static port => port.HasValue).Select(static port => port!.Value).Distinct()];
    }

    private static int? ParsePort(string url)
    {
        try
        {
            return BindingAddress.Parse(url).Port;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> Split(string? configuredValue) =>
    [
        .. (configuredValue ?? string.Empty)
            .Split(UrlSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
    ];

    private static IEnumerable<string> ExpandPorts(string? configuredPorts, string scheme) =>
        Split(configuredPorts).Select(port => $"{scheme}://*:{port}");
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>Decides whether the application's own addresses have to be restated so the listeners composition opens cannot take them away.</summary>
/// <remarks>
/// <para>
/// Kestrel ignores the URL-shaped addresses — <c>ASPNETCORE_URLS</c>, <c>ASPNETCORE_HTTP_PORTS</c>, and everything that
/// reaches <c>WebApplication.Urls</c> — as soon as any listener is bound in code, so a deployment that states its
/// application port that way loses it the moment a second endpoint opens a socket beside it.
/// <see cref="ConfiguredApplicationListeners.AsKestrelEndpointConfiguration" /> is what hands those addresses back to
/// the framework; this is what decides when it has to.
/// </para>
/// <para>
/// The decision belongs to the whole composition rather than to any one endpoint, which is the defect this closes:
/// taken inside a single endpoint's branch it is skipped exactly when that endpoint is the one switched off and another
/// is the one binding, and the process then starts successfully while serving nothing on the address its clients
/// connect to. Every endpoint that opens a listener of its own is read here, so an endpoint added later is folded into
/// this one decision rather than restating the addresses from a branch of its own.
/// </para>
/// </remarks>
internal static class ApplicationListenerRestatement
{
    /// <summary>Reports whether the application's addresses have to be restated as Kestrel endpoints before the server is built.</summary>
    /// <param name="configuration">The application configuration, read at the root because the Kestrel section is not nested under this product's own.</param>
    /// <param name="mcpEndpointSettings">The MCP endpoint's settings, whose HTTPS profiles bind listeners of their own.</param>
    /// <param name="healthEndpointSettings">The probe endpoint's settings, whose listener is bound in code whenever it is enabled.</param>
    /// <param name="adminEndpointSettings">The administrative endpoint's settings, whose listener is bound in code whenever it is enabled.</param>
    /// <returns><see langword="true" /> when the addresses would otherwise be dropped, otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static bool IsRequired(
        IConfiguration configuration,
        McpEndpointOptions mcpEndpointSettings,
        HealthEndpointOptions healthEndpointSettings,
        AdminEndpointOptions adminEndpointSettings)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(mcpEndpointSettings);
        ArgumentNullException.ThrowIfNull(healthEndpointSettings);
        ArgumentNullException.ThrowIfNull(adminEndpointSettings);

        // The MCP HTTPS profiles bind in code as well and are deliberately not counted here. They replace the
        // application listener instead of joining it, so restating the addresses beside them would reopen the
        // clear-text socket those profiles exist to close.
        var listenerBoundInCode = healthEndpointSettings.Enabled || adminEndpointSettings.Enabled;

        // Where the addresses were already going to be ignored, restating them opens a listener the deployment had
        // replaced rather than keeping one it still has.
        var addressesAlreadyReplaced =
            mcpEndpointSettings.BindsOwnListeners || ConfiguredKestrelEndpoints.AnyConfigured(configuration);

        return listenerBoundInCode && !addressesAlreadyReplaced;
    }
}

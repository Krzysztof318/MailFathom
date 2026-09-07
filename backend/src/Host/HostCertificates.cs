// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.Security.Transport;

namespace MailFathom.Host;

/// <summary>Loads the TLS material every surface this deployment terminates TLS on will present.</summary>
/// <remarks>
/// <para>
/// A callable step rather than lines in the top-level statements that used to hold it, for the reason
/// <see cref="HostComposition" /> is one: statements written there cannot be called, so nothing could assert that a
/// surface whose store the composition registers is a surface the startup path loads. It was not asserted and the
/// client surface was not loaded — the store was registered and consulted, the listener bound, and every handshake on
/// its port was refused by a lookup that had never been filled.
/// </para>
/// <para>
/// It runs after the container exists and before the server is started, which is what makes unusable material a startup
/// failure with nothing listening rather than a listener that binds and then refuses per connection.
/// </para>
/// </remarks>
internal static class HostCertificates
{
    /// <summary>Names the certificate store of every surface this deployment terminates TLS on.</summary>
    /// <param name="surfaces">What the composition settled about the surfaces this deployment serves.</param>
    /// <returns>One service key per store, <see langword="null" /> naming the unkeyed store the protocol surface owns.</returns>
    /// <remarks>
    /// Separate from <see cref="LoadAsync" /> so that a test can hold this list against the stores
    /// <see cref="HostComposition" /> actually registered. That comparison is the whole guard: a surface added to the
    /// registration and to the handshake lookup but not here fails the suite instead of shipping a mute listener.
    /// <para>
    /// The argument is guarded by <see cref="LoadAsync" /> rather than here, because an iterator defers its own guard to
    /// the first enumeration and would therefore report the fault at whichever line happened to enumerate it.
    /// </para>
    /// </remarks>
    internal static IEnumerable<object?> TlsTerminatingStoreKeys(ComposedHostSurfaces surfaces)
    {
        if (surfaces.Mcp is { Enabled: true, TerminatesTls: true })
        {
            yield return null;
        }

        if (surfaces.Admin is { Enabled: true, TerminatesTls: true })
        {
            yield return HostComposition.AdminCertificateStoreKey;
        }

        if (surfaces.Client is { Enabled: true, TerminatesTls: true })
        {
            yield return HostComposition.ClientEndpointCertificateStoreKey;
        }
    }

    /// <summary>Loads every store above, and the probe surface's own certificate.</summary>
    /// <param name="services">The built container the stores were registered in.</param>
    /// <param name="surfaces">What the composition settled about the surfaces this deployment serves.</param>
    /// <param name="cancellationToken">Cancels the material retrieval.</param>
    /// <returns>A task that completes once every profile this deployment presents has a usable identity.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The probe certificate is loaded unconditionally because its holder answers for its own surface being unserved or
    /// clear-text, which the stores above cannot: each of them is one endpoint's material and an endpoint that
    /// terminates no TLS has none to load.
    /// </remarks>
    internal static async Task LoadAsync(
        IServiceProvider services,
        ComposedHostSurfaces surfaces,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(surfaces);

        foreach (var storeKey in TlsTerminatingStoreKeys(surfaces))
        {
            var store = storeKey is null
                ? services.GetRequiredService<TransportServerCertificateStore>()
                : services.GetRequiredKeyedService<TransportServerCertificateStore>(storeKey);

            await store.LoadAsync(cancellationToken);
        }

        await services.GetRequiredService<HealthEndpointCertificate>().LoadAsync(cancellationToken);
    }
}

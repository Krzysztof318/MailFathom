// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>Configures what a surface's clear-text socket does while that surface also terminates TLS.</summary>
/// <remarks>
/// <para>
/// A redirect protects the next request rather than the one that arrived. An API key sent in clear text is already on
/// the wire by the time a redirect is written, and nothing recovers it. The listener exists so that a client still
/// pointed at <c>http://</c> is told where the endpoint is instead of failing on a connection refused or an unreadable
/// handshake error, which is what enabling TLS otherwise looks like from the outside. While it redirects it is not a
/// supported way to reach the surface, which is why it answers nothing else: no route is mapped on it, and no
/// authentication, rate-limiting, CORS, or client-certificate handler runs for a request that arrived there.
/// </para>
/// <para>
/// On unless a deployment turns it off, and meaningful only under <see cref="EndpointTransport.HttpAndHttps" />, which
/// is the one mode with both a clear-text socket and somewhere to send what arrives on it. Turning it off leaves that
/// same socket serving the routes, which is the deliberate both-schemes posture rather than the migration one.
/// <see cref="EndpointTransport.HttpsOnly" /> opens no clear-text socket for this to describe and
/// <see cref="EndpointTransport.Http" /> terminates no TLS to redirect to, so a deployment that writes this section
/// under either is refused at startup — the setting would otherwise read as configured while nothing acted on it.
/// </para>
/// <para>
/// The socket itself is the surface's own <c>BindAddress</c> and <c>Port</c>, and there is no address here to state
/// again. A surface has one clear-text socket; which of two things it does with it is the whole of this section.
/// </para>
/// </remarks>
internal sealed class TransportClearTextRedirectOptions
{
    /// <summary>Gets or sets whether the clear-text socket answers every request with the address of the TLS one.</summary>
    /// <remarks>Enabled, so enabling TLS does not read as an outage to a client nobody had repointed yet. Turning it off leaves the same socket serving the surface's routes in clear text.</remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets whether the deployment wrote this section, as opposed to inheriting the value above.</summary>
    /// <remarks>
    /// It is what tells a redirect an operator asked for from the default one, which is the difference between a startup
    /// error and silence on a surface with no clear-text socket to describe. The binder cannot answer it — an absent
    /// section and one carrying only defaults bind identically — so it is read from configuration by the surface that
    /// owns the section.
    /// </remarks>
    internal bool WasStated { get; private set; }

    /// <summary>Records that the deployment wrote this section.</summary>
    /// <remarks>Called by the surface's own read, which is the only place that holds the configuration section this was bound from.</remarks>
    internal void MarkStated() => this.WasStated = true;
}

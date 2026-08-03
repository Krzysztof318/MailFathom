// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Net.Security;
using System.Security.Authentication;
using MailFathom.Host.Configuration;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace MailFathom.Host.Security;

/// <summary>Binds the configured HTTPS profiles onto Kestrel listeners.</summary>
/// <remarks>
/// <para>
/// Binding a listener explicitly is what replaces whatever URLs the host was otherwise configured with, which is the
/// mechanism behind this section's promise that no clear-text listener stays open behind an HTTPS profile. It applies
/// to everything the process serves, the health endpoints included, because one Kestrel serves them all.
/// </para>
/// <para>
/// Each profile's identity and TLS floor are settled per connection, from the server name in the client's hello,
/// through <see cref="TlsHandshakeCallbackOptions" />. A per-connection callback is used rather than a certificate
/// selector because a selector answers with a certificate alone: it cannot present the explicit chain a client needs to
/// build a path to a root, and it cannot give one profile a stricter TLS floor than its neighbour on the same address.
/// </para>
/// <para>
/// Whether a client certificate is asked for is settled here too, but it is one answer for the whole endpoint rather
/// than a per-profile one. Trust profiles identify a client application, and a deployment has at most one of those, so
/// scoping them per domain would add a second place to say who is trusted and nothing a deployment would use.
/// </para>
/// <para>
/// The set of HTTP versions belongs to the listener rather than to the connection, because ALPN offers what the
/// listener was bound with and HTTP/3 is a second socket that is either opened or not — both decided before any server
/// name is known. Profiles sharing an address are therefore required to agree on it, which the section validates.
/// </para>
/// </remarks>
internal static class TransportHttpsEndpointBinder
{
    /// <summary>Binds one Kestrel listener per address the configured profiles name.</summary>
    /// <param name="kestrelOptions">The server options being composed.</param>
    /// <param name="httpsSettings">The validated HTTPS profiles.</param>
    /// <param name="certificateStore">The store the handshake reads its identities from.</param>
    /// <param name="requestClientCertificates">Whether the handshake asks the client for a certificate, which it does when any client certificate profile is configured.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static void Bind(
        KestrelServerOptions kestrelOptions,
        TransportHttpsOptions httpsSettings,
        TransportServerCertificateStore certificateStore,
        bool requestClientCertificates)
    {
        ArgumentNullException.ThrowIfNull(kestrelOptions);
        ArgumentNullException.ThrowIfNull(httpsSettings);
        ArgumentNullException.ThrowIfNull(certificateStore);

        foreach (var listener in httpsSettings.Endpoints.GroupBy(static endpoint => endpoint.ListenerAddress))
        {
            var address = listener.Key;
            var servedProtocols = listener.First().ServedHttpProtocols;

            kestrelOptions.Listen(address.Address, address.Port, listenOptions =>
            {
                listenOptions.Protocols = MapProtocols(servedProtocols);
                listenOptions.UseHttps(new TlsHandshakeCallbackOptions
                {
                    OnConnection = context => SelectIdentity(
                        certificateStore,
                        address,
                        servedProtocols,
                        requestClientCertificates,
                        context),
                });
            });
        }
    }

    /// <summary>Answers one client hello with the profile that publishes the name it asked for.</summary>
    /// <remarks>
    /// A name no profile publishes ends the connection instead of receiving a default certificate, which is what keeps
    /// an operator's other domain — or an unrelated scan — from being handed an identity it never asked for. The
    /// refusal names the listener only: the server name came from the client and is not written into a log line on its
    /// say-so.
    /// </remarks>
    [SuppressMessage("Security", "CA5359:Do not disable certificate validation", Justification = "CA5359 reads this callback as a client deciding to trust the server it dialled, where accepting everything defeats TLS. This is the server side of the handshake and the certificate is the client's: refusing here would end the connection for the private authority a trust profile names, and accepting here grants nothing, because whether the certificate identifies a client this deployment serves is decided afterwards by McpClientCertificateValidation against that profile's own anchors, expected names, and required usage. It is set only when a profile exists to make that decision, and it is the same posture HttpsConnectionAdapterOptions.ClientCertificateValidation states for a listener built from a URL.")]
    private static ValueTask<SslServerAuthenticationOptions> SelectIdentity(
        TransportServerCertificateStore certificateStore,
        TransportHttpsListenerAddress listener,
        IReadOnlyList<TransportHttpProtocol> servedProtocols,
        bool requestClientCertificates,
        TlsHandshakeCallbackContext context)
    {
        if (certificateStore.Find(listener, context.ClientHelloInfo.ServerName) is not { } identity)
        {
            throw new AuthenticationException(
                $"No MCP HTTPS profile on {listener.Address}:{listener.Port} publishes the server name this connection asked for.");
        }

        var authenticationOptions = new SslServerAuthenticationOptions
        {
            ServerCertificateContext = identity.CertificateContext,
            EnabledSslProtocols = identity.EnabledSslProtocols,

            // Asked for rather than demanded, and only when a trust profile exists to judge one. Demanding it here
            // would end the handshake for a client without a certificate, which says nothing an operator can read and
            // nothing a client can act on; asking lets that client reach the middleware and be refused there, or served
            // there when every profile is Optional. This is the same posture ConfigureHttpsDefaults states for a
            // listener configured from a URL — and it has to be restated here, because a listener that supplies its own
            // SslServerAuthenticationOptions never consults those defaults.
            ClientCertificateRequired = requestClientCertificates,
        };

        if (requestClientCertificates)
        {
            // Accepting every certificate at this level grants nothing: whether one is trusted is decided against the
            // profile's own anchors, by McpClientCertificateValidation. Left unset, the platform would judge against
            // the machine's trust store instead, which is both too narrow — it fails the private authority a profile
            // names — and too wide, since it accepts any public authority the machine happens to trust.
            authenticationOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
        }

        // Left unset rather than set empty when this listener negotiates nothing over TLS, which is an HTTP/3-only
        // profile: its versions travel over QUIC, and an empty offer is not the same statement as making none.
        if (NegotiableProtocols(servedProtocols) is { Count: > 0 } applicationProtocols)
        {
            authenticationOptions.ApplicationProtocols = applicationProtocols;
        }

        return ValueTask.FromResult(authenticationOptions);
    }

    /// <summary>Maps the configured versions onto the flags a Kestrel listener is bound with.</summary>
    private static HttpProtocols MapProtocols(IReadOnlyList<TransportHttpProtocol> servedProtocols) =>
        servedProtocols.Aggregate(
            HttpProtocols.None,
            static (mapped, protocol) => mapped | protocol switch
            {
                TransportHttpProtocol.Http1 => HttpProtocols.Http1,
                TransportHttpProtocol.Http2 => HttpProtocols.Http2,
                TransportHttpProtocol.Http3 => HttpProtocols.Http3,
                _ => HttpProtocols.None,
            });

    /// <summary>Lists what ALPN offers on this TLS connection, most preferred first.</summary>
    /// <remarks>
    /// HTTP/3 is absent by design even when it is served: it is carried by QUIC on its own socket and advertised to
    /// clients through an alternative-service header, so offering it here would name a version this connection cannot
    /// switch to.
    /// </remarks>
    private static List<SslApplicationProtocol> NegotiableProtocols(IReadOnlyList<TransportHttpProtocol> servedProtocols)
    {
        var negotiable = new List<SslApplicationProtocol>(capacity: 2);

        if (servedProtocols.Contains(TransportHttpProtocol.Http2))
        {
            negotiable.Add(SslApplicationProtocol.Http2);
        }

        if (servedProtocols.Contains(TransportHttpProtocol.Http1))
        {
            negotiable.Add(SslApplicationProtocol.Http11);
        }

        return negotiable;
    }
}

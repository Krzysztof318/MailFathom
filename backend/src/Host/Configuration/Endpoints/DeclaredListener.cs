// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>One socket a surface asks for, described by everything that socket rather than a route decides.</summary>
/// <param name="SectionName">The configuration section that asked for it, which every disagreement is reported against.</param>
/// <param name="Surface">The surface served on it.</param>
/// <param name="BindAddress">The configured bind address, as written.</param>
/// <param name="Port">The TCP port.</param>
/// <param name="TerminatesTls">Whether the socket carries TLS.</param>
/// <param name="RedirectsClearText">Whether a clear-text socket answers with the address of the TLS one instead of serving routes.</param>
/// <param name="PresentsProfiles">Whether the TLS identity is selected from HTTPS profiles by server name, as opposed to being one certificate the socket always presents.</param>
/// <param name="Profiles">The HTTPS profiles bound here, empty unless <paramref name="PresentsProfiles" /> is set.</param>
/// <param name="RequestsClientCertificates">Whether the handshake asks the client for a certificate.</param>
/// <param name="RedirectTargets">Where each domain this surface publishes is served over TLS, read by a redirecting clear-text socket and empty on every other one.</param>
/// <remarks>
/// Two surfaces may name one port, which is what lets a single-node deployment publish one socket rather than three.
/// What they may not do is disagree about it: a socket is clear text or it is TLS, it redirects or it serves the routes,
/// it asks for a client certificate or it does not, and it presents identities one way. Every field here is one of those
/// answers, which is why they are gathered into a value that can simply be compared rather than checked setting by
/// setting at each call site.
/// </remarks>
internal sealed record DeclaredListener(
    string SectionName,
    ServedSurfaces Surface,
    string BindAddress,
    int Port,
    bool TerminatesTls,
    bool RedirectsClearText,
    bool PresentsProfiles,
    IReadOnlyList<TransportHttpsEndpointOptions> Profiles,
    bool RequestsClientCertificates,
    IReadOnlyDictionary<string, int> RedirectTargets);

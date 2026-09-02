// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>Which schemes a surface is served under.</summary>
/// <remarks>
/// <para>
/// One socket serves one scheme, so serving both means two listeners. What supplies the TLS half differs by surface and
/// is that surface's own business: the MCP and administrative endpoints name HTTPS profiles, each with its own domain,
/// certificate, and TLS floor, while the probes carry one certificate and one port. The question is the same for all
/// three, which is why one type answers it.
/// </para>
/// <para>
/// Clear text is the default, so adopting a release costs no certificate work. TLS is an upgrade a deployment takes
/// deliberately, and <see cref="HttpsOnly" /> is what takes it in full: no clear-text socket stays open behind a profile
/// serving the same routes without the protection that profile was configured to add.
/// </para>
/// </remarks>
internal enum EndpointTransport
{
    /// <summary>Serve the surface over clear-text HTTP alone, and terminate no TLS.</summary>
    Http = 0,

    /// <summary>Serve the surface over both schemes, on a socket each.</summary>
    /// <remarks>
    /// On the request-serving surfaces the clear-text socket redirects to the TLS one unless a deployment turns the
    /// redirect off, which is what makes enabling TLS safe for a client nobody has repointed yet without leaving the
    /// routes reachable in clear text by accident.
    /// </remarks>
    HttpAndHttps = 1,

    /// <summary>Serve the surface over TLS alone, and open no clear-text socket at all.</summary>
    HttpsOnly = 2,
}

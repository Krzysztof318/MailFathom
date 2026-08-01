// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailFathom.Host.Configuration;

/// <summary>Which schemes the health-endpoint listener is opened under.</summary>
/// <remarks>
/// <para>
/// One socket serves one scheme, which is why the two-scheme member needs a second port rather than a flag. The set is
/// closed at three members because those are the three postures a probe network has: clear text on a network the
/// operator already trusts, TLS everywhere, and the interval during which a deployment is moving from the first to the
/// second and both have to answer.
/// </para>
/// <para>
/// Clear text is the default, so adopting the release costs no certificate work. TLS is an upgrade a deployment takes
/// deliberately, and taking it never leaves a clear-text listener behind: <see cref="HttpsOnly" /> opens no such socket.
/// </para>
/// </remarks>
internal enum HealthEndpointTransport
{
    /// <summary>Serve the probes over clear-text HTTP on the configured port, and open no TLS listener.</summary>
    Http = 0,

    /// <summary>Serve the probes over clear-text HTTP on the configured port and over TLS on the configured HTTPS port.</summary>
    HttpAndHttps = 1,

    /// <summary>Serve the probes over TLS on the configured port, and open no clear-text listener.</summary>
    HttpsOnly = 2,
}

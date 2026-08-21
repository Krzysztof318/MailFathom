// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>One HTTP version an HTTPS endpoint serves.</summary>
/// <remarks>
/// <para>
/// A set of these rather than a flags enum, because configuration binds a list far more legibly than it binds a
/// combined numeric value, and because the repository's enum rule reserves contiguous values from zero — a flags
/// layout would have to break it. The set is mapped onto Kestrel's own flags at the point the listener is configured.
/// </para>
/// <para>
/// The versions are not interchangeable in how they are carried. HTTP/1.1 and HTTP/2 share the TLS connection and are
/// chosen by ALPN during the handshake; HTTP/3 runs over QUIC on UDP, needs the platform to supply QUIC, and always
/// uses TLS 1.3 whatever floor the endpoint configures.
/// </para>
/// </remarks>
internal enum TransportHttpProtocol
{
    /// <summary>HTTP/1.1, which every MCP client speaks.</summary>
    Http1 = 0,

    /// <summary>HTTP/2, negotiated by ALPN on the same TLS connection.</summary>
    Http2 = 1,

    /// <summary>HTTP/3 over QUIC, which the host platform must support and which a client reaches through an alternative service advertisement.</summary>
    Http3 = 2,
}

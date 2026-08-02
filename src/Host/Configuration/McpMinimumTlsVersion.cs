// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration;

/// <summary>The oldest TLS version an HTTPS endpoint completes a handshake with.</summary>
/// <remarks>
/// <para>
/// Two members rather than a mapping onto every protocol the platform can name. TLS 1.0 and 1.1 are deprecated by
/// RFC 8996 and SSL before them is broken, so neither is a floor an operator may select — and a setting that could
/// express them would be a way to weaken the endpoint rather than to configure it. The choice that remains is real:
/// 1.2 is what interoperates, and 1.3 is what a deployment able to require it should.
/// </para>
/// <para>
/// It is a floor, not a selection. Naming <see cref="Tls12" /> still negotiates TLS 1.3 with a client that offers it,
/// so the setting decides what is refused rather than what is preferred.
/// </para>
/// </remarks>
internal enum McpMinimumTlsVersion
{
    /// <summary>Accept TLS 1.2 and TLS 1.3, which is what interoperates with every current client.</summary>
    Tls12 = 0,

    /// <summary>Accept TLS 1.3 only, refusing the clients and middleboxes that cannot offer it.</summary>
    Tls13 = 1,
}

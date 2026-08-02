// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Security;
using System.Security.Authentication;

namespace MailFathom.Host.Security;

/// <summary>What one HTTPS profile contributes to a TLS handshake it is selected for.</summary>
/// <param name="ProfileName">The operator-chosen profile name, which is what a diagnostic about this handshake reports.</param>
/// <param name="CertificateContext">The leaf, its private key, and the intermediates presented after it.</param>
/// <param name="EnabledSslProtocols">The TLS versions this profile completes a handshake with.</param>
/// <remarks>
/// The certificate context is built once per profile and reused for every connection, because building one costs a
/// chain construction that has no reason to happen per handshake. The authentication options handed to the platform are
/// built per connection from it, since the platform is free to mutate what it is given.
/// </remarks>
internal sealed record McpTlsEndpointIdentity(
    string ProfileName,
    SslStreamCertificateContext CertificateContext,
    SslProtocols EnabledSslProtocols);

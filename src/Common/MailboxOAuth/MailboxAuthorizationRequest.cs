// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Common.MailboxOAuth;

/// <summary>Everything one interactive authorization run needs to obtain a refresh token for a mailbox.</summary>
/// <param name="AuthorizationEndpoint">The authorization server's endpoint a person signs in at, unused by the device-code grant.</param>
/// <param name="TokenEndpoint">The endpoint the authorization code or device code is exchanged at.</param>
/// <param name="DeviceAuthorizationEndpoint">The RFC 8628 endpoint that issues a device code, or <see langword="null" /> when the provider offers none.</param>
/// <param name="ClientId">The registered application's client identifier.</param>
/// <param name="ClientSecret">The registered application's secret, or <see langword="null" /> for a public client that authenticates with PKCE alone.</param>
/// <param name="Scope">The space-delimited scopes to request.</param>
/// <param name="RedirectUri">The loopback address the authorization code is returned to, unused by the device-code grant.</param>
/// <remarks>
/// The type is public because the command-line tool composes it, and it is the only shape of this work that crosses
/// the assembly boundary. It carries no resolved secret: a client secret typed at a terminal is a
/// <see cref="string" /> before it reaches here, which is a property of an interactive tool rather than something this
/// type could improve on.
/// </remarks>
public sealed record MailboxAuthorizationRequest(
    Uri? AuthorizationEndpoint,
    Uri TokenEndpoint,
    Uri? DeviceAuthorizationEndpoint,
    string ClientId,
    string? ClientSecret,
    string Scope,
    Uri? RedirectUri)
{
    /// <inheritdoc />
    /// <remarks>Redacted by construction, because the record carries a client secret.</remarks>
    public override string ToString() => "***";
}

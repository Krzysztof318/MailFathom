// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Authorization;

/// <summary>What an authorization server issued for one operator's session with one deployment.</summary>
/// <param name="AccessToken">The credential every request presents, spent when <paramref name="AccessTokenExpiresAt" /> passes.</param>
/// <param name="RefreshToken">The credential a spent access token is exchanged for a new one with, which is what the session's length actually is.</param>
/// <param name="AccessTokenExpiresAt">When the access token stops being accepted, as this machine's clock reads it.</param>
/// <remarks>
/// <para>
/// A refresh token is required rather than optional. Without one the session would be exactly as long as the first
/// access token — typically an hour — and the operator would meet that as a command failing rather than as a session
/// ending, so a grant that carries none is refused where it is issued instead of being stored and discovered later.
/// </para>
/// <para>
/// The expiry is an instant on this machine's clock rather than the server's stated lifetime, because a lifetime is
/// only meaningful relative to when the answer arrived. It is read back before every request, which is the whole
/// mechanism by which renewal is silent.
/// </para>
/// </remarks>
internal sealed record DeploymentGrant(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt)
{
    /// <inheritdoc />
    /// <remarks>Redacted by construction, because both members are credentials.</remarks>
    public override string ToString() => "***";
}

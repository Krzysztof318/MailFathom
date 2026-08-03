// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.Resolution;

namespace MailFathom.Infrastructure.Mail.OAuth;

/// <summary>The secrets one token request resolved from an account's configured references.</summary>
/// <param name="ClientSecret">The registered application's secret, or <see langword="null" /> for a public client, which holds none.</param>
/// <param name="RefreshToken">The operator-provisioned refresh token, or <see langword="null" /> for the client-credentials grant, which has none.</param>
/// <remarks>
/// The instance is owned by the token request that resolved it and is disposed when that request ends, which bounds
/// the window in which a process dump could contain either value to one request rather than to process uptime. This
/// mirrors <see cref="MailAccountConnectionMaterial" />, and for the same reason: a credential rotated behind an
/// unchanged reference is picked up by the next request with no cache to invalidate.
/// </remarks>
public sealed record MailOAuthClientMaterial(ResolvedSecret? ClientSecret, ResolvedSecret? RefreshToken) : IDisposable
{
    /// <inheritdoc />
    public void Dispose()
    {
        this.ClientSecret?.Dispose();
        this.RefreshToken?.Dispose();
    }
}

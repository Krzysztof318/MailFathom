// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailFathom.Infrastructure.Persistence;

/// <summary>Which settings a deployment provisions its database credential through.</summary>
/// <param name="HasConnectionStringSecret">Whether <c>Persistence:ConnectionString</c> is configured.</param>
/// <param name="HasPasswordSecret">Whether <c>Persistence:Password</c> is configured.</param>
/// <remarks>
/// This is the shape of the provisioning, not the credential itself, and it is deliberately derived from which blocks
/// exist rather than from what they resolve to. Answering it costs no retrieval, which is what lets a reload be
/// refused before anything is read.
/// </remarks>
internal readonly record struct DatabaseCredentialSourceShape(
    bool HasConnectionStringSecret,
    bool HasPasswordSecret)
{
    /// <summary>Reads the shape out of configured connection settings.</summary>
    /// <param name="connectionSettings">The settings to read.</param>
    /// <returns>The provisioning shape they describe.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="connectionSettings" /> is <see langword="null" />.</exception>
    internal static DatabaseCredentialSourceShape Of(PostgresConnectionSettings connectionSettings)
    {
        ArgumentNullException.ThrowIfNull(connectionSettings);

        return new DatabaseCredentialSourceShape(
            connectionSettings.ConnectionStringSecret is not null,
            connectionSettings.Password is not null);
    }
}

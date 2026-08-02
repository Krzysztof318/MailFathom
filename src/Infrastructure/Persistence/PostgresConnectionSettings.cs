// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets;

namespace MailFathom.Infrastructure.Persistence;

/// <summary>Names where the PostgreSQL connection string and its password come from.</summary>
/// <param name="ConfiguredConnectionString">
/// The connection string from ordinary configuration, typically <c>ConnectionStrings:mailfathom</c>, or
/// <see langword="null" /> when <paramref name="ConnectionStringSecret" /> supplies it.
/// </param>
/// <param name="ConnectionStringSecret">
/// The block referencing a complete connection string held in a secret store, or <see langword="null" /> when ordinary
/// configuration supplies it. A connection string is more than a password, so a store-backed deployment usually keeps
/// it whole and rotates one artifact rather than splitting the credential across two systems.
/// </param>
/// <param name="Password">
/// The block referencing the password alone, or <see langword="null" /> when the connection string already carries
/// every credential or the deployment authenticates without one.
/// </param>
/// <remarks>
/// The three sources are deliberately independent settings rather than one switch, because a deployment picks the
/// shape its provisioning system already has. Supplying a password twice is rejected during composition; supplying no
/// connection string at all is rejected there too.
/// </remarks>
public sealed record PostgresConnectionSettings(
    string? ConfiguredConnectionString,
    ConfiguredSecret? ConnectionStringSecret,
    ConfiguredSecret? Password);

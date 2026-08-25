// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Settings;

/// <summary>Reads the persisted configuration once, before the host has a container to resolve anything from.</summary>
/// <remarks>
/// <para>
/// The host composes its configuration before it composes its services, so the layer between the deployment's files
/// and the operator's overrides has to be readable at a moment when nothing is registered. Everything the read needs
/// is therefore built here and torn down again: the secret adapters that turn the configured references into
/// credentials, and one data source that opens a single connection.
/// </para>
/// <para>
/// Where a secret block supplies the credential — a password block, or a block referencing a whole connection string —
/// it is supplied per physical connection through Npgsql's own callback rather than baked into the connection string,
/// exactly as the long-lived pool supplies it, so bootstrap keeps no credential in memory after the connection it
/// authenticated. A deployment that instead writes the password into <c>Persistence:ConnectionString</c> or
/// <c>ConnectionStrings:mailfathom</c> as ordinary configuration is passed through unchanged, and the credential is in
/// the connection string here exactly as it is in the pool's; <c>PostgresConnectionStringProvider</c> reports that
/// shape as a configuration diagnostic and this read makes no attempt to improve on it. The data source is disposed
/// before this returns either way, so no pool outlives the one read it existed for.
/// </para>
/// <para>
/// Both legs that reach the server are bounded, because they run before any endpoint is open and therefore before
/// anything could report that the host is still starting. The command carries the deployment's own command timeout,
/// and the connect attempt carries the driver's default wherever the connection string left it infinite: a server
/// that accepts TCP without finishing the handshake, or accepts the connection and then never answers the statement,
/// would otherwise hold the process there for as long as it stayed silent, and the caller composing configuration has
/// no deadline of its own to fall back on.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
public static class RootSettingsBootstrap
{
    /// <summary>Reads the deployment's persisted configuration document.</summary>
    /// <param name="connectionSettings">Where the connection string and its credential come from, read from the sources beneath this layer.</param>
    /// <param name="interpretation">How the deployment's configured secret-bearing values are interpreted.</param>
    /// <param name="commandTimeout">How long the single read may run before the driver cancels it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The document and the version it was read at.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="connectionSettings" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interpretation" /> is not a supported mode, or <paramref name="commandTimeout" /> is not positive.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the configured connection settings do not describe a database that can be opened.</exception>
    /// <exception cref="RootSettingsUnreadableException">Thrown when the persisted configuration cannot be read at all.</exception>
    public static async Task<RootSettingsDocument> ReadAsync(
        PostgresConnectionSettings connectionSettings,
        SecretValueInterpretation interpretation,
        TimeSpan commandTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connectionSettings);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(commandTimeout, TimeSpan.Zero);

        // The registration is reused rather than repeated, so a deployment that gains a managed-store scheme adapter
        // gains it here too instead of resolving one set of schemes at bootstrap and another once the host is running.
        // The clock is what the host's own container would have supplied: the file adapter bounds how long a read of
        // credential material may take, and that bound is not one bootstrap gets to skip.
        await using var secretResolution = new ServiceCollection()
            .AddSingleton(TimeProvider.System)
            .AddSecretResolution(interpretation)
            .BuildServiceProvider();

        var resolver = secretResolution.GetRequiredService<ISecretReferenceResolver>();

        var composed = await ConnectionStringComposer.ComposeAsync(
            connectionSettings.ConfiguredConnectionString,
            connectionSettings.ConnectionStringSecret,
            connectionSettings.Password,
            resolver,
            cancellationToken);

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(composed.ConnectionSettings.ConnectionString);

        // Written over whatever the connection string said, exactly as the long-lived pool's enrichment writes it over
        // the same keyword: `Persistence:CommandTimeoutSeconds` is where the deployment states that bound, and a
        // `Command Timeout=0` left in the connection string would mean this read has none at all.
        //
        // Rounded up and capped rather than truncated, because the keyword is whole seconds and this method admits any
        // positive bound: a sub-second one would truncate to `0` and a bound past `int.MaxValue` seconds would wrap,
        // and either would land on the very value the assignment exists to overwrite.
        dataSourceBuilder.ConnectionStringBuilder.CommandTimeout =
            (int)Math.Min(Math.Ceiling(commandTimeout.TotalSeconds), int.MaxValue);

        // The connection leg is bounded for the same reason and has no configured bound of its own, so what it gets is
        // the driver's own default in place of an infinite wait: `Timeout=0` means a connect attempt that never gives
        // up, and a server accepting TCP without finishing the startup handshake would hold a starting host there. It
        // is replaced only for this one read — the long-lived pool keeps whatever the deployment configured, where a
        // process that is already serving can report the wait.
        if (dataSourceBuilder.ConnectionStringBuilder.Timeout == 0)
        {
            dataSourceBuilder.ConnectionStringBuilder.Timeout = new NpgsqlConnectionStringBuilder().Timeout;
        }

        ConnectionStringComposer.SupplyThePasswordPerConnection(
            dataSourceBuilder,
            composed.PasswordSource,
            token => ConnectionStringComposer.ResolveCurrentPasswordAsync(
                composed.PasswordSource,
                connectionSettings.ConnectionStringSecret,
                connectionSettings.Password,
                resolver,
                token));

        await using var dataSource = dataSourceBuilder.Build();

        return await new RootSettingsDocumentReader(dataSource).ReadAsync(cancellationToken);
    }
}

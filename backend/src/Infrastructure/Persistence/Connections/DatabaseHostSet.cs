// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Connections;

/// <summary>Reads how many PostgreSQL instances a connection string names, and what that decides about the pool built from it.</summary>
/// <remarks>
/// <para>
/// A deployment running PostgreSQL replicated has two ways to be reached. It can put one address in front of the
/// cluster that follows the primary through a failover — CloudNativePG's <c>-rw</c> Service is that, and MailFathom
/// needs nothing for it, because one address is one host like any other. Or it can name the instances themselves, which
/// is what survives that address being unavailable, and then the driver is what finds the primary among them.
/// </para>
/// <para>
/// Npgsql builds a different pool for the second shape, and it is not a setting on the first one:
/// <see cref="NpgsqlDataSourceBuilder.Build" /> refuses a connection string naming more than one host, and
/// <see cref="NpgsqlDataSourceBuilder.BuildMultiHost" /> is what accepts it. Which of the two runs is therefore read off
/// the connection string rather than configured beside it, so a deployment states its topology once, where it already
/// states the address.
/// </para>
/// <para>
/// <b>Nothing here routes a read to a standby.</b> Every session MailFathom opens reaches the primary, whichever
/// instance that currently is. Serving a query from a replica is a decision about read-your-writes and replication lag
/// that
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0001-application-owned-repositories-for-persistence-ports.md">ADR 0001</see>
/// would have to be amended for, rather than a keyword to accept here.
/// </para>
/// </remarks>
internal static class DatabaseHostSet
{
    /// <summary>The target session attributes that guarantee the session the driver settles on accepts a write.</summary>
    /// <remarks>
    /// Npgsql's remaining values all permit a standby. <c>any</c> takes whichever host answered first, and the two
    /// <c>prefer-</c> values fall back to exactly that when no primary is reachable — which is the moment a deployment
    /// most needs to be told rather than to carry on read-only. <c>primary</c> refuses a server in hot standby, and
    /// <c>read-write</c> refuses that and a session whose <c>default_transaction_read_only</c> is on, so it is the
    /// stronger of the two and both are accepted.
    /// </remarks>
    private static readonly string[] AttributesThatGuaranteeAWritableSession = ["primary", "read-write"];

    /// <summary>Reports whether the connection settings name more than one PostgreSQL instance.</summary>
    /// <param name="connectionSettings">The composed connection settings.</param>
    /// <returns><see langword="true" /> when several instances are named.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="connectionSettings" /> is <see langword="null" />.</exception>
    internal static bool NamesSeveralInstances(NpgsqlConnectionStringBuilder connectionSettings)
    {
        ArgumentNullException.ThrowIfNull(connectionSettings);

        return connectionSettings.Host?.Contains(',', StringComparison.Ordinal) == true;
    }

    /// <summary>Builds the data source the configured topology calls for.</summary>
    /// <param name="dataSourceBuilder">The builder every other connection decision has already been applied to.</param>
    /// <returns>A pool over one instance, or one that finds the primary among several.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dataSourceBuilder" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A single-host deployment gets exactly the pool it got before this existed. The multi-host pool is only built
    /// where the connection string asked for it, because it selects among clusters on every open and probes a server's
    /// role to do so, and a deployment naming one instance has nothing for either to decide.
    /// </remarks>
    internal static NpgsqlDataSource BuildDataSource(NpgsqlDataSourceBuilder dataSourceBuilder)
    {
        ArgumentNullException.ThrowIfNull(dataSourceBuilder);

        return NamesSeveralInstances(dataSourceBuilder.ConnectionStringBuilder)
            ? dataSourceBuilder.BuildMultiHost()
            : dataSourceBuilder.Build();
    }

    /// <summary>Refuses connection settings that name several instances without guaranteeing the driver reaches a writable one.</summary>
    /// <param name="connectionSettings">The composed connection settings.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="connectionSettings" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when several instances are named and the target session attributes permit a hot standby.</exception>
    /// <remarks>
    /// This is refused at startup rather than left to the first write, because the failure it prevents is silent for as
    /// long as nothing writes. A process that settled on a standby comes up, answers every read, and reports itself
    /// healthy; the synchronization run that arrives later fails on a read-only transaction, which reads as a database
    /// defect rather than as the connection string it is.
    /// </remarks>
    internal static void RefuseAHostSetThatCouldSettleOnAStandby(NpgsqlConnectionStringBuilder connectionSettings)
    {
        ArgumentNullException.ThrowIfNull(connectionSettings);

        if (!NamesSeveralInstances(connectionSettings))
        {
            return;
        }

        var targetSessionAttributes = connectionSettings.TargetSessionAttributes;

        if (targetSessionAttributes is not null
            && AttributesThatGuaranteeAWritableSession.Contains(targetSessionAttributes, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        // The configured value is deliberately not in the message. It is read from a connection string that may have
        // arrived as one whole secret, and a diagnostic that quoted part of it back would be the one place a credential
        // could reach a log through a setting that has nothing to do with credentials.
        throw new InvalidOperationException(
            "The database connection string names several PostgreSQL instances while its Target Session Attributes does not guarantee a writable session. MailFathom writes to the instance it connects to, so a value that permits a hot standby would leave every read working and the first write failing. Set Target Session Attributes to primary or to read-write, or name one address that follows the primary instead.");
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Owners;

/// <summary>Turns the driver's answer to a failed owner-record read into the operator's diagnosis.</summary>
/// <remarks>
/// <para>
/// A pure function over the exception the read raised, deliberately separate from the reader that observes it: the
/// read itself needs a database and is therefore proved by the integration suite, while what the operator is told to
/// go and look at is a decision a unit test can state directly. Every message names a place to look and no value read
/// from configuration, so nothing here can carry a credential, a host name, or a database name — and none of them
/// carries the owner's document either, which is that person's configuration rather than a diagnostic.
/// </para>
/// <para>
/// It sits beside <c>RootSettingsReadFailures</c> rather than sharing it because the two reads fail into different
/// situations. That one runs while the host composes its settings, so every arm ends in a process that refuses to
/// start; this one runs while a request is being served for one person, so every arm ends in a request that failed
/// while the deployment goes on running — and the table each sends the operator to is a different table.
/// </para>
/// <para>
/// Two entry points rather than one, because the driver reports a connect timeout, an exhausted pool, and a statement
/// the server never answered as the same shape, and the two answers are opposites: one says the database could not be
/// reached, the other says it was reached and did not finish. Only the reader knows which stage raised what it
/// caught, so each stage asks the question its own failure can answer.
/// </para>
/// </remarks>
internal static class OwnerSettingsReadFailures
{
    /// <summary>What a database this read never reached is reported as, whichever stage met it.</summary>
    private const string Unreachable =
        "The database holding the owner records could not be reached, so the request naming that owner was refused rather than answered from a record MailFathom does not have.";

    /// <summary>Diagnoses a read refused once the statement had been issued.</summary>
    /// <param name="exception">The exception the statement raised.</param>
    /// <returns>What the operator is told, which names the one place the correction is made.</returns>
    /// <remarks>
    /// This entry point is reached only with a connection the server had already accepted, which is what lets its
    /// timeout arm say the server answered everything up to the statement. A bound that expired while the connection
    /// was still being opened is the other entry point's, and it is the opposite diagnosis.
    /// </remarks>
    internal static string DiagnoseWhileReading(NpgsqlException exception) =>
        WhatTheServerAnswered(exception)
        ?? exception switch
        {
            // The bound on the statement expiring, which the driver raises with a `TimeoutException` inside it. The
            // server accepted the connection and answered everything up to the statement, so this is the one failure
            // here that says nothing about whether the database can be reached.
            { InnerException: TimeoutException } =>
                "The statement reading an owner's record did not finish within the configured command timeout. The database server was reached and accepted the connection, so what to look at is Persistence:CommandTimeoutSeconds and whatever is holding the settings_accounts row, rather than the network.",

            // A connection that broke while the statement was in flight, which arrives carrying no state at all
            // because the server never got to answer one.
            _ => Unreachable,
        };

    /// <summary>Diagnoses a read refused before the statement could be issued, while the connection was being opened.</summary>
    /// <param name="exception">The exception opening the connection raised.</param>
    /// <returns>What the operator is told, which names the one place the correction is made.</returns>
    /// <remarks>
    /// A timeout at this stage is a database that could not be reached within the bound, or a pool with nothing left
    /// to hand out, rather than a server holding a statement it never answered — which is why it falls to the
    /// unreachable answer instead of to the command timeout above. The states a server answers a connection attempt
    /// with are diagnosed exactly as they are on the statement, because the correction is the same one.
    /// </remarks>
    internal static string DiagnoseWhileConnecting(NpgsqlException exception) =>
        WhatTheServerAnswered(exception) ?? Unreachable;

    /// <summary>Says what a state the server answered with means, or nothing when the server answered no state at all.</summary>
    private static string? WhatTheServerAnswered(NpgsqlException exception) => exception switch
    {
        // The server answered and holds no such database, which is provisioning that never ran rather than anything
        // about the network.
        PostgresException { SqlState: PostgresErrorCodes.InvalidCatalogName } =>
            "The database server carries no database of the configured name, so no owner record could be read. Create it, or correct the name in the connection settings: the server was reached and answered, so neither the network nor the credential is what refused MailFathom.",

        // How a database missing this build's migration answers.
        PostgresException { SqlState: PostgresErrorCodes.UndefinedTable } =>
            "The database does not carry the settings_accounts table this build reads an owner's record from. Apply the migrations this build defines: until they are applied the request is refused while the deployment goes on running.",

        // How a wrong or rotated credential answers: the server was reached, and what it refused was the password.
        PostgresException { SqlState: PostgresErrorCodes.InvalidPassword } =>
            "The database holding the owner records refused the configured credential. Check the Persistence secret block rather than the network: the server answered, and what it rejected is the password MailFathom composed for it.",

        // The server answered and refused the connection outright, which is a `pg_hba.conf` with no rule admitting
        // this role from this host to this database.
        PostgresException { SqlState: PostgresErrorCodes.InvalidAuthorizationSpecification } =>
            "The database server admits no connection for the configured role, host, and database, so no owner record could be read. Its authorization rules are what refused MailFathom, so the credential and the network are both beside the point.",

        // What a correctly reachable database says when the schema was applied by one role and is served by another. A
        // per-table grant on an existing deployment does not cover a table a later release adds, which is exactly the
        // shape this table arrives in.
        PostgresException { SqlState: PostgresErrorCodes.InsufficientPrivilege } =>
            "The serving role holds no privilege on settings_accounts. Grant it on the table an owner's record lives in, the way the schema documentation describes for a schema applied by one role and served by another.",

        _ => null,
    };
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Owners;

/// <summary>Turns the driver's answer to a refused owner-record write into the operator's diagnosis.</summary>
/// <remarks>
/// <para>
/// The sibling of <see cref="OwnerSettingsReadFailures" /> and separate from it for the reason the two exceptions are:
/// the states of the server are the same states, and what an operator does about each one is not. A read is refused to
/// somebody asking about a record; a write is refused to somebody who had already read one, so the commoner correction
/// is the privilege on the one statement this build issues against the row.
/// </para>
/// <para>
/// Which entry point a failure arrives at is the caller's decision rather than the exception's, because the driver
/// reports a failure before the statement and one during it as the same shapes and the two are opposite diagnoses.
/// Only the writer knows whether the statement was ever issued, which is the whole of what separates "nothing was
/// written" from "whether it applied is not known".
/// </para>
/// <para>
/// Pure over the exception, so what an operator is told is decided in the unit suite while the statement itself is
/// proved against a real server. Every message names a place to look and no value read from configuration, so nothing
/// here can carry a credential, a host name, a database name, or any part of somebody's record.
/// </para>
/// </remarks>
internal static class OwnerSettingsWriteFailures
{
    private const string Unreachable =
        "The database holding the owner records could not be reached, so nothing was written. The owner's record is exactly what it was, and the write is safe to attempt again.";

    /// <summary>What both of the write's uncertain endings say, so the two cannot drift into two answers.</summary>
    private const string WhetherItApplied =
        "whether it applied is not known from here. Read the owner's record to find out which version is now in force, and attempt the write again over the version it was composed on — which the version guard refuses if the first attempt did commit.";

    /// <summary>Diagnoses a write refused once the statement had been issued.</summary>
    /// <param name="exception">The exception the statement raised.</param>
    /// <returns>What the operator is told, which names the one place the correction is made.</returns>
    /// <remarks>
    /// An arm reading a state the server answered with says the statement was refused before it applied, so the record
    /// is exactly what it was. Neither of the other two can: by the time this is reached the statement had been sent.
    /// </remarks>
    internal static string Diagnose(NpgsqlException exception) =>
        WhatTheServerAnswered(exception)
        ?? exception switch
        {
            // The bound on the statement expiring. Unlike every arm the server answered, this one cannot say the row
            // stood still: the server accepted the statement and did not answer within the bound.
            { InnerException: TimeoutException } =>
                $"The statement writing an owner's record did not finish within the configured command timeout, so {WhetherItApplied} What to look at is Persistence:CommandTimeoutSeconds and whatever is holding the settings_accounts row, rather than the network.",

            // A connection that broke while the statement was in flight, which arrives carrying no state at all
            // because the server never got to answer one.
            _ =>
                $"The connection to the database was lost while the statement writing an owner's record was in flight, so {WhetherItApplied} What to look at is the network and the database's own log, rather than anything MailFathom composed.",
        };

    /// <summary>Diagnoses a write refused before the statement could be issued, while the connection was being opened.</summary>
    /// <param name="exception">The exception opening the connection raised.</param>
    /// <returns>What the operator is told, which names the one place the correction is made.</returns>
    /// <remarks>
    /// Nothing here leaves the commit undecided, which is the whole reason this is a second entry point: no statement
    /// was sent, so the row certainly stood still, and a timeout at this stage is a database that could not be reached
    /// within the bound rather than one holding a statement it never answered.
    /// </remarks>
    internal static string DiagnoseWhileConnecting(NpgsqlException exception) =>
        WhatTheServerAnswered(exception) ?? Unreachable;

    /// <summary>Says what a state the server answered with means, or nothing when the server answered no state at all.</summary>
    private static string? WhatTheServerAnswered(NpgsqlException exception) => exception switch
    {
        // The server answered and holds no such database, which is provisioning that never ran rather than anything
        // about the network.
        PostgresException { SqlState: PostgresErrorCodes.InvalidCatalogName } =>
            "The database server carries no database of the configured name, so nothing was written. Create it, or correct the name in the connection settings: the server was reached and answered, so neither the network nor the credential is what refused MailFathom.",

        // How a database missing this build's migration answers.
        PostgresException { SqlState: PostgresErrorCodes.UndefinedTable } =>
            "The database does not carry the settings_accounts table an owner's record is written to, so nothing was written. Apply the migrations this build defines.",

        // How a wrong or rotated credential answers: the server was reached, and what it refused was the password.
        PostgresException { SqlState: PostgresErrorCodes.InvalidPassword } =>
            "The database holding the owner records refused the configured credential, so nothing was written. Check the Persistence secret block rather than the network: the server answered, and what it rejected is the password MailFathom composed for it.",

        // The server answered and refused the connection outright, which is a `pg_hba.conf` with no rule admitting
        // this role from this host to this database.
        PostgresException { SqlState: PostgresErrorCodes.InvalidAuthorizationSpecification } =>
            "The database server admits no connection for the configured role, host, and database, so nothing was written. Its authorization rules are what refused MailFathom, so the credential and the network are both beside the point.",

        // The commonest of them on a working deployment: a role granted SELECT on the table serves every start and
        // refuses every write.
        PostgresException { SqlState: PostgresErrorCodes.InsufficientPrivilege } =>
            "The serving role holds no privilege to update settings_accounts, so nothing was written. Grant UPDATE on the table an owner's record lives in; a role granted only SELECT serves every start and refuses every write, which is why this is the first failure a deployment meets here and never meets on a read.",

        // A standby, or a session under `default_transaction_read_only`. It exists on this path and not on the read's
        // because it is the one failure a statement's direction alone produces.
        PostgresException { SqlState: PostgresErrorCodes.ReadOnlySqlTransaction } =>
            "The database refuses to write in this session, so nothing was written. MailFathom is connected to a standby or to a session that is read-only by default; point the connection at the primary, or lift the read-only default for the serving role.",

        // The server answered and refused, with a state none of the arms above names. Saying which state it gave is
        // the whole of what this side knows, and it is what separates a statement to correct from a database to
        // reach: a value the column cannot hold, a full disk, a deadlock.
        PostgresException { SqlState: { Length: > 0 } sqlState } =>
            $"The database refused the statement that would have written the owner's record, answering SQLSTATE {sqlState}, so nothing was written. The server was reached and answered, so what to correct is the statement's own subject rather than the network; the provider's own text is the inner exception.",

        _ => null,
    };
}

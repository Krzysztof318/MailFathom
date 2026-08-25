// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Settings;

/// <summary>Turns the driver's answer to a failed persisted-configuration read into the operator's diagnosis.</summary>
/// <remarks>
/// This is a pure function over the exception the read raised, deliberately separate from the reader that observes it:
/// the read itself needs a database and is therefore proved by the integration suite, while what the operator is told
/// to go and look at is a decision a unit test can state directly. Every message names a place to look and no value
/// read from configuration, so nothing here can carry a credential, a host name, or a database name.
/// </remarks>
internal static class RootSettingsReadFailures
{
    /// <summary>Diagnoses a failed read of the persisted configuration.</summary>
    /// <param name="exception">The exception the read raised.</param>
    /// <returns>What the operator is told, which names the one place the correction is made.</returns>
    /// <remarks>
    /// <para>
    /// The six recognized failures are the ones a deployment actually meets on the way to its first successful read,
    /// and each sends the operator somewhere different. Five are states the server reported; the sixth is the bound on
    /// the statement expiring, which the driver raises with no state at all. What is left over is a database that
    /// could not be reached, which is also what an ordinary transport failure is, so both share the last arm.
    /// </para>
    /// <para>
    /// The privilege arm runs ahead of the schema gate that used to be the first to meet that condition, because a
    /// per-table grant on an existing deployment does not cover a table a later release adds. Making the same
    /// diagnosis here is what keeps that case from arriving as a database that appears unreachable.
    /// </para>
    /// </remarks>
    internal static string Diagnose(NpgsqlException exception) => exception switch
    {
        // The server answered and holds no such database, which is provisioning that never ran rather than anything
        // about the network. This read meets it before the schema gate does, and the gate collapses it into a reason
        // class beside two others, so saying it plainly is worth an arm of its own.
        PostgresException { SqlState: PostgresErrorCodes.InvalidCatalogName } =>
            "The database server carries no database of the configured name. Create it, or correct the name in the connection settings: the server was reached and answered, so neither the network nor the credential is what refused MailFathom.",

        // How a database missing this build's migration answers.
        PostgresException { SqlState: PostgresErrorCodes.UndefinedTable } =>
            "The database does not carry the settings_root table this build reads its persisted configuration from. Apply the migrations this build defines and start the host again.",

        // How a wrong or rotated credential answers: the server was reached, and what it refused was the password.
        PostgresException { SqlState: PostgresErrorCodes.InvalidPassword } =>
            "The database holding the persisted configuration refused the configured credential. Check the Persistence secret block rather than the network: the server answered, and what it rejected is the password MailFathom composed for it.",

        // The server answered and refused the connection outright, which is a `pg_hba.conf` with no rule admitting
        // this role from this host to this database — the commoner of the two authorization failures on a new
        // deployment, and one nothing about the network would explain.
        PostgresException { SqlState: PostgresErrorCodes.InvalidAuthorizationSpecification } =>
            "The database server admits no connection for the configured role, host, and database. Its authorization rules are what refused MailFathom, so the credential and the network are both beside the point.",

        // What a correctly reachable database says when the schema was applied by one role and is served by another.
        PostgresException { SqlState: PostgresErrorCodes.InsufficientPrivilege } =>
            "The serving role holds no privilege on settings_root. Grant it on the table the persisted configuration lives in, the way the schema documentation describes for a schema applied by one role and served by another.",

        // The bound on the statement expiring, which the driver raises with a `TimeoutException` inside it. The server
        // answered everything up to the statement, so it is the one failure here that says nothing about whether the
        // database can be reached — a lock held on the row, or an instance with nothing left to answer with.
        { InnerException: TimeoutException } =>
            "The statement reading the persisted configuration did not finish within the configured command timeout. The database server was reached and accepted the connection, so what to look at is Persistence:CommandTimeoutSeconds and whatever is holding the settings_root row, rather than the network.",

        _ => "The database holding the persisted configuration could not be reached. MailFathom composes its settings from that layer before it opens any endpoint, so it refuses to start on the sources beneath it.",
    };
}

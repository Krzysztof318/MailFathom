// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Settings;

/// <summary>Turns the driver's answer to a refused persisted-configuration write into the operator's diagnosis.</summary>
/// <remarks>
/// <para>
/// The sibling of <see cref="RootSettingsReadFailures" /> and separate from it for the reason the two exceptions are
/// separate: the failures are the same states of the same server, and what an operator does about each one is not. A
/// read is refused before a start, so its corrections are about reaching the database at all; a write is refused by a
/// running deployment that already read the row, so the commoner correction is the privilege on the one statement
/// this build issues against it.
/// </para>
/// <para>
/// Pure over the exception, so what the operator is told is decided in the unit suite while the statement itself is
/// proved against a real server. Every message names a place to look and no value read from configuration, so nothing
/// here can carry a credential, a host name, or a database name.
/// </para>
/// </remarks>
internal static class RootSettingsWriteFailures
{
    /// <summary>Diagnoses a refused write of the persisted configuration.</summary>
    /// <param name="exception">The exception the statement raised.</param>
    /// <returns>What the operator is told, which names the one place the correction is made.</returns>
    /// <remarks>
    /// Every arm but the timeout one says the statement was refused before it applied, so the deployment's settings are
    /// exactly what they were. The timeout arm deliberately does not: the server accepted the statement and stopped
    /// answering, so whether the row moved is unknown from here — and the version guard is what makes that safe to
    /// resolve by reading rather than by guessing, since a retry over the version the write was composed on is refused
    /// as superseded if the first attempt did commit.
    /// </remarks>
    internal static string Diagnose(NpgsqlException exception) => exception switch
    {
        // The server answered and holds no such database, which is provisioning that never ran rather than anything
        // about the network.
        PostgresException { SqlState: PostgresErrorCodes.InvalidCatalogName } =>
            "The database server carries no database of the configured name, so nothing was written. Create it, or correct the name in the connection settings: the server was reached and answered, so neither the network nor the credential is what refused MailFathom.",

        // How a database missing this build's migration answers.
        PostgresException { SqlState: PostgresErrorCodes.UndefinedTable } =>
            "The database does not carry the settings_root table the persisted configuration is written to, so nothing was written. Apply the migrations this build defines.",

        // How a wrong or rotated credential answers: the server was reached, and what it refused was the password.
        PostgresException { SqlState: PostgresErrorCodes.InvalidPassword } =>
            "The database holding the persisted configuration refused the configured credential, so nothing was written. Check the Persistence secret block rather than the network: the server answered, and what it rejected is the password MailFathom composed for it.",

        // The server answered and refused the connection outright, which is a `pg_hba.conf` with no rule admitting this
        // role from this host to this database.
        PostgresException { SqlState: PostgresErrorCodes.InvalidAuthorizationSpecification } =>
            "The database server admits no connection for the configured role, host, and database, so nothing was written. Its authorization rules are what refused MailFathom, so the credential and the network are both beside the point.",

        // The commonest of them on a working deployment, and the one that separates this diagnosis from the read's: a
        // role granted SELECT on the table serves every start and refuses every write.
        PostgresException { SqlState: PostgresErrorCodes.InsufficientPrivilege } =>
            "The serving role holds no privilege to update settings_root, so nothing was written. Grant UPDATE on the table the persisted configuration lives in; a role granted only SELECT serves every start and refuses every write, which is why this is the first failure a deployment meets here and never meets on a read.",

        // A standby, or a session under `default_transaction_read_only`. It exists on this path and not on the read's
        // because it is the one failure a statement's direction alone produces.
        PostgresException { SqlState: PostgresErrorCodes.ReadOnlySqlTransaction } =>
            "The database refuses to write in this session, so nothing was written. MailFathom is connected to a standby or to a session that is read-only by default; point the connection at the primary, or lift the read-only default for the serving role.",

        // The bound on the statement expiring. Unlike every arm above, this one cannot say the row stood still: the
        // server accepted the statement and did not answer within the bound.
        { InnerException: TimeoutException } =>
            "The statement writing the persisted configuration did not finish within the configured command timeout, so whether it applied is not known from here. Read the version now in force to find out, and attempt the write again over the version it was composed on — which the version guard refuses if the first attempt did commit. What to look at is Persistence:CommandTimeoutSeconds and whatever is holding the settings_root row, rather than the network.",

        // The server answered and refused, with a state none of the arms above names. Saying which state it gave is the
        // whole of what this side knows, and it is what separates a statement to correct from a database to reach: a
        // value the column cannot hold (a NUL in a configuration value renders as `22P05`), a full disk, a deadlock.
        // Reported as its own arm rather than absorbed below, because the sentence below sends an operator to the
        // network and tells them to retry a write that may never succeed.
        PostgresException { SqlState: { Length: > 0 } sqlState } =>
            $"The database refused the statement that would have persisted the configuration write, answering SQLSTATE {sqlState}, so nothing was written. The server was reached and answered, so what to correct is the statement's own subject rather than the network; the provider's own text is the inner exception.",

        _ => "The database holding the persisted configuration could not be reached, so nothing was written. The deployment goes on serving the configuration it already composed, and the write is safe to attempt again.",
    };
}

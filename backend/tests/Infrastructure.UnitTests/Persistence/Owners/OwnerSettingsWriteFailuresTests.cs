// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Owners;
using Npgsql;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Owners;

/// <summary>
/// Covers what a failed write of one owner's record tells the operator. A write says more than a read has to, because
/// the operator's next act depends on whether the row moved: an arm the server answered proves it did not, and the two
/// that arrive with no answer at all cannot, so they send somebody to read the version rather than to attempt the write
/// again blind.
/// </summary>
public sealed class OwnerSettingsWriteFailuresTests
{
    /// <summary>The commonest failure a working deployment meets here, and one it never meets on a read.</summary>
    [Fact]
    public void Diagnose_PrivilegeRefused_SendsTheOperatorToTheUpdateGrant()
    {
        // Arrange
        var exception = ProviderFailure(PostgresErrorCodes.InsufficientPrivilege);

        // Act
        var diagnosis = OwnerSettingsWriteFailures.Diagnose(exception);

        // Assert
        Assert.Contains("Grant UPDATE", diagnosis, StringComparison.Ordinal);
        Assert.Contains("nothing was written", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>The one failure a statement's direction alone produces, which is why it exists on this path and not on the read's.</summary>
    [Fact]
    public void Diagnose_ReadOnlySession_SendsTheOperatorToThePrimary()
    {
        // Arrange
        var exception = ProviderFailure(PostgresErrorCodes.ReadOnlySqlTransaction);

        // Act
        var diagnosis = OwnerSettingsWriteFailures.Diagnose(exception);

        // Assert
        Assert.Contains("standby", diagnosis, StringComparison.Ordinal);
        Assert.Contains("nothing was written", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>A table this build's migration has not reached is the migrations rather than anything to check over.</summary>
    [Fact]
    public void Diagnose_TableDoesNotExist_SendsTheOperatorToTheMigrations()
    {
        // Arrange
        var exception = ProviderFailure(PostgresErrorCodes.UndefinedTable);

        // Act
        var diagnosis = OwnerSettingsWriteFailures.Diagnose(exception);

        // Assert
        Assert.Contains("settings_accounts", diagnosis, StringComparison.Ordinal);
        Assert.Contains("Apply the migrations", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>
    /// The distinction the whole type exists for. A state the server answered proves the statement was refused before
    /// it applied, so the operator is told the record stood still; a bound that expired with the statement in flight
    /// proves nothing of the kind, and telling somebody to attempt it again blind is how a change lands twice.
    /// </summary>
    [Fact]
    public void Diagnose_CommandTimeoutExpired_LeavesTheCommitUndecidedAndSaysHowToSettleIt()
    {
        // Arrange
        var exception = new NpgsqlException("exception while reading from stream", new TimeoutException());

        // Act
        var diagnosis = OwnerSettingsWriteFailures.Diagnose(exception);

        // Assert
        Assert.Contains("whether it applied is not known from here", diagnosis, StringComparison.Ordinal);
        Assert.Contains("Persistence:CommandTimeoutSeconds", diagnosis, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing was written", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>A connection lost mid-statement is the other ending that cannot say the row stood still.</summary>
    [Fact]
    public void Diagnose_ConnectionLostWhileTheStatementWasInFlight_LeavesTheCommitUndecided()
    {
        // Arrange
        var exception = new NpgsqlException("exception while writing to stream");

        // Act
        var diagnosis = OwnerSettingsWriteFailures.Diagnose(exception);

        // Assert
        Assert.Contains("whether it applied is not known from here", diagnosis, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing was written", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reason connecting is a second entry point: no statement was sent, so the row certainly stood still, and a
    /// bound that expired here is a database that could not be reached rather than one holding a statement.
    /// </summary>
    [Fact]
    public void DiagnoseWhileConnecting_TimeoutExpired_ReportsTheRecordAsUnchanged()
    {
        // Arrange
        var exception = new NpgsqlException("the connection pool has been exhausted", new TimeoutException());

        // Act
        var diagnosis = OwnerSettingsWriteFailures.DiagnoseWhileConnecting(exception);

        // Assert
        Assert.Contains("exactly what it was", diagnosis, StringComparison.Ordinal);
        Assert.DoesNotContain("whether it applied", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>A state the server answered means the same thing whichever stage met it: the statement never applied.</summary>
    [Fact]
    public void DiagnoseWhileConnecting_AStateTheServerAnswered_SaysWhatTheStateMeansRatherThanThatItWasUnreachable()
    {
        // Arrange
        var exception = ProviderFailure(PostgresErrorCodes.InvalidPassword);

        // Act
        var diagnosis = OwnerSettingsWriteFailures.DiagnoseWhileConnecting(exception);

        // Assert
        Assert.Contains("Persistence secret block", diagnosis, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be reached", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>An unrecognized state names the state and nothing else, which is what separates a statement to correct from a database to reach.</summary>
    [Fact]
    public void Diagnose_UnrecognizedProviderState_NamesTheStateAndSendsTheOperatorToTheStatement()
    {
        // Arrange
        var exception = ProviderFailure(PostgresErrorCodes.DiskFull);

        // Act
        var diagnosis = OwnerSettingsWriteFailures.Diagnose(exception);

        // Assert
        Assert.Contains(PostgresErrorCodes.DiskFull, diagnosis, StringComparison.Ordinal);
        Assert.Contains("rather than the network", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>
    /// No diagnosis carries anything the server said. What a write to this table meets is a message naming the
    /// database, the role, or the table, and one of those repeated into a request log is a deployment describing
    /// itself to whoever reads it.
    /// </summary>
    [Theory]
    [InlineData(PostgresErrorCodes.InvalidCatalogName)]
    [InlineData(PostgresErrorCodes.UndefinedTable)]
    [InlineData(PostgresErrorCodes.InvalidPassword)]
    [InlineData(PostgresErrorCodes.InvalidAuthorizationSpecification)]
    [InlineData(PostgresErrorCodes.InsufficientPrivilege)]
    [InlineData(PostgresErrorCodes.ReadOnlySqlTransaction)]
    [InlineData(PostgresErrorCodes.DiskFull)]
    public void Diagnose_AnyRecognizedOrUnrecognizedState_CarriesNothingTheServerSaid(string sqlState)
    {
        // Arrange
        var exception = new PostgresException(
            messageText: "permission denied for table settings_accounts of role \"mailfathom\" at host db.internal",
            severity: "FATAL",
            invariantSeverity: "FATAL",
            sqlState: sqlState);

        // Act
        var diagnosis = OwnerSettingsWriteFailures.Diagnose(exception);

        // Assert
        Assert.DoesNotContain("db.internal", diagnosis, StringComparison.Ordinal);
        Assert.DoesNotContain("mailfathom\"", diagnosis, StringComparison.Ordinal);
    }

    private static PostgresException ProviderFailure(string sqlState) => new(
        messageText: "the server said something",
        severity: "FATAL",
        invariantSeverity: "FATAL",
        sqlState: sqlState);
}

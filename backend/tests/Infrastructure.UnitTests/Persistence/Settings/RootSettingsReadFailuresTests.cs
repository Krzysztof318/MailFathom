// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Settings;
using Npgsql;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Settings;

/// <summary>
/// Covers what a failed read of the persisted configuration tells the operator. The read itself needs a database, but
/// which of six places it sends somebody to is a decision, and every one of them is a first install or an upgrade
/// that has gone wrong — where sending the reader to the network instead of to a grant is the difference between a
/// deployment repaired in a minute and one nobody can diagnose.
/// </summary>
public sealed class RootSettingsReadFailuresTests
{
    /// <summary>
    /// A database that was never created is provisioning rather than the network, and this read is the first thing to
    /// meet it: the schema gate below would collapse it into a reason class beside two others.
    /// </summary>
    [Fact]
    public void Diagnose_DatabaseDoesNotExist_SendsTheOperatorToTheProvisioning()
    {
        // Arrange
        var exception = ProviderFailure(PostgresErrorCodes.InvalidCatalogName);

        // Act
        var diagnosis = RootSettingsReadFailures.Diagnose(exception);

        // Assert
        Assert.Contains("no database of the configured name", diagnosis, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be reached", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>A database missing this build's migration is told to apply the migrations rather than checked over.</summary>
    [Fact]
    public void Diagnose_TableDoesNotExist_SendsTheOperatorToTheMigrations()
    {
        // Arrange
        var exception = ProviderFailure(PostgresErrorCodes.UndefinedTable);

        // Act
        var diagnosis = RootSettingsReadFailures.Diagnose(exception);

        // Assert
        Assert.Contains("settings_root", diagnosis, StringComparison.Ordinal);
        Assert.Contains("Apply the migrations", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>A rejected password is the secret block's, and saying so is what keeps the network out of the search.</summary>
    [Fact]
    public void Diagnose_PasswordRefused_SendsTheOperatorToTheSecretBlock()
    {
        // Arrange
        var exception = ProviderFailure(PostgresErrorCodes.InvalidPassword);

        // Act
        var diagnosis = RootSettingsReadFailures.Diagnose(exception);

        // Assert
        Assert.Contains("Persistence secret block", diagnosis, StringComparison.Ordinal);
        Assert.Contains("rather than the network", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>
    /// A connection no authorization rule admits is neither the credential nor the network, which is the one thing an
    /// operator meeting it on a new deployment most needs told.
    /// </summary>
    [Fact]
    public void Diagnose_ConnectionNotAdmitted_SaysTheCredentialAndTheNetworkAreBesideThePoint()
    {
        // Arrange
        var exception = ProviderFailure(PostgresErrorCodes.InvalidAuthorizationSpecification);

        // Act
        var diagnosis = RootSettingsReadFailures.Diagnose(exception);

        // Assert
        Assert.Contains("admits no connection", diagnosis, StringComparison.Ordinal);
        Assert.Contains("beside the point", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>A schema applied by one role and served by another is a grant, and this read meets it before the schema gate does.</summary>
    [Fact]
    public void Diagnose_PrivilegeRefused_SendsTheOperatorToTheGrant()
    {
        // Arrange
        var exception = ProviderFailure(PostgresErrorCodes.InsufficientPrivilege);

        // Act
        var diagnosis = RootSettingsReadFailures.Diagnose(exception);

        // Assert
        Assert.Contains("no privilege on settings_root", diagnosis, StringComparison.Ordinal);
        Assert.Contains("Grant it", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>
    /// The bound expiring says nothing about whether the database can be reached, so it is sorted apart from the
    /// failure that does: the server accepted the connection and answered everything up to the statement.
    /// </summary>
    [Fact]
    public void Diagnose_CommandTimeoutExpired_SendsTheOperatorToTheBoundRatherThanTheNetwork()
    {
        // Arrange
        var exception = new NpgsqlException("exception while reading from stream", new TimeoutException());

        // Act
        var diagnosis = RootSettingsReadFailures.Diagnose(exception);

        // Assert
        Assert.Contains("Persistence:CommandTimeoutSeconds", diagnosis, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be reached", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>An unrecognized state is a database that could not be read, and it says nothing it cannot support.</summary>
    [Fact]
    public void Diagnose_UnrecognizedProviderState_ReportsTheDatabaseAsUnreached()
    {
        // Arrange
        var exception = ProviderFailure(PostgresErrorCodes.SyntaxError);

        // Act
        var diagnosis = RootSettingsReadFailures.Diagnose(exception);

        // Assert
        Assert.Contains("could not be reached", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>A failure that never reached PostgreSQL carries no state at all, and shares the unreachable answer.</summary>
    [Fact]
    public void Diagnose_TransportFailureCarryingNoProviderState_ReportsTheDatabaseAsUnreached()
    {
        // Arrange
        var exception = new NpgsqlException("connection refused");

        // Act
        var diagnosis = RootSettingsReadFailures.Diagnose(exception);

        // Assert
        Assert.Contains("could not be reached", diagnosis, StringComparison.Ordinal);
        Assert.Contains("before it opens any endpoint", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>
    /// No diagnosis carries anything read from configuration. The connection settings are the one thing this read
    /// composes, so a message quoting one would put a host name, a database name, or worse into a startup log.
    /// </summary>
    [Theory]
    [InlineData(PostgresErrorCodes.InvalidCatalogName)]
    [InlineData(PostgresErrorCodes.UndefinedTable)]
    [InlineData(PostgresErrorCodes.InvalidPassword)]
    [InlineData(PostgresErrorCodes.InvalidAuthorizationSpecification)]
    [InlineData(PostgresErrorCodes.InsufficientPrivilege)]
    [InlineData(PostgresErrorCodes.SyntaxError)]
    public void Diagnose_AnyRecognizedOrUnrecognizedState_CarriesNothingTheServerSaid(string sqlState)
    {
        // Arrange
        var exception = new PostgresException(
            messageText: "password authentication failed for user \"mailfathom\" at host db.internal",
            severity: "FATAL",
            invariantSeverity: "FATAL",
            sqlState: sqlState);

        // Act
        var diagnosis = RootSettingsReadFailures.Diagnose(exception);

        // Assert
        Assert.DoesNotContain("db.internal", diagnosis, StringComparison.Ordinal);
        Assert.DoesNotContain("mailfathom\"", diagnosis, StringComparison.Ordinal);
    }

    private static PostgresException ProviderFailure(string sqlState) =>
        new(
            messageText: "the server said something",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: sqlState);
}

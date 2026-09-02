// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Owners;
using Npgsql;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Owners;

/// <summary>
/// Covers what a failed read of one owner's record tells the operator. The read itself needs a database, but which
/// place it sends somebody to is a decision — and this read runs while a request is being served rather than while
/// the host starts, so the sentence is what somebody has to work from with no startup failure to read beside it.
/// </summary>
public sealed class OwnerSettingsReadFailuresTests
{
    /// <summary>A table this build's migration has not reached is the migrations rather than anything to check over.</summary>
    [Fact]
    public void DiagnoseWhileReading_TableDoesNotExist_SendsTheOperatorToTheMigrations()
    {
        // Arrange
        var exception = ProviderFailure(PostgresErrorCodes.UndefinedTable);

        // Act
        var diagnosis = OwnerSettingsReadFailures.DiagnoseWhileReading(exception);

        // Assert
        Assert.Contains("settings_accounts", diagnosis, StringComparison.Ordinal);
        Assert.Contains("Apply the migrations", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>
    /// A grant is where this one most often lands, because a per-table grant made on an earlier release does not
    /// cover a table a later one adds — which is exactly the shape settings_accounts arrives in.
    /// </summary>
    [Fact]
    public void DiagnoseWhileReading_PrivilegeRefused_SendsTheOperatorToTheGrant()
    {
        // Arrange
        var exception = ProviderFailure(PostgresErrorCodes.InsufficientPrivilege);

        // Act
        var diagnosis = OwnerSettingsReadFailures.DiagnoseWhileReading(exception);

        // Assert
        Assert.Contains("no privilege on settings_accounts", diagnosis, StringComparison.Ordinal);
        Assert.Contains("Grant it", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>A rejected password is the secret block's, and saying so is what keeps the network out of the search.</summary>
    [Fact]
    public void DiagnoseWhileReading_PasswordRefused_SendsTheOperatorToTheSecretBlock()
    {
        // Arrange
        var exception = ProviderFailure(PostgresErrorCodes.InvalidPassword);

        // Act
        var diagnosis = OwnerSettingsReadFailures.DiagnoseWhileReading(exception);

        // Assert
        Assert.Contains("Persistence secret block", diagnosis, StringComparison.Ordinal);
        Assert.Contains("rather than the network", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>A connection no authorization rule admits is neither the credential nor the network.</summary>
    [Fact]
    public void DiagnoseWhileReading_ConnectionNotAdmitted_SaysTheCredentialAndTheNetworkAreBesideThePoint()
    {
        // Arrange
        var exception = ProviderFailure(PostgresErrorCodes.InvalidAuthorizationSpecification);

        // Act
        var diagnosis = OwnerSettingsReadFailures.DiagnoseWhileReading(exception);

        // Assert
        Assert.Contains("admits no connection", diagnosis, StringComparison.Ordinal);
        Assert.Contains("beside the point", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>A database that was never created is provisioning rather than the network.</summary>
    [Fact]
    public void DiagnoseWhileReading_DatabaseDoesNotExist_SendsTheOperatorToTheProvisioning()
    {
        // Arrange
        var exception = ProviderFailure(PostgresErrorCodes.InvalidCatalogName);

        // Act
        var diagnosis = OwnerSettingsReadFailures.DiagnoseWhileReading(exception);

        // Assert
        Assert.Contains("no database of the configured name", diagnosis, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be reached", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>
    /// The bound expiring says nothing about whether the database can be reached, so it is sorted apart from the
    /// failure that does: the server accepted the connection and answered everything up to the statement.
    /// </summary>
    [Fact]
    public void DiagnoseWhileReading_CommandTimeoutExpired_SendsTheOperatorToTheBoundRatherThanTheNetwork()
    {
        // Arrange
        var exception = new NpgsqlException("exception while reading from stream", new TimeoutException());

        // Act
        var diagnosis = OwnerSettingsReadFailures.DiagnoseWhileReading(exception);

        // Assert
        Assert.Contains("Persistence:CommandTimeoutSeconds", diagnosis, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be reached", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>An unrecognized state is a database that could not be read, and it says nothing it cannot support.</summary>
    [Fact]
    public void DiagnoseWhileReading_UnrecognizedProviderState_ReportsTheDatabaseAsUnreached()
    {
        // Arrange
        var exception = ProviderFailure(PostgresErrorCodes.SyntaxError);

        // Act
        var diagnosis = OwnerSettingsReadFailures.DiagnoseWhileReading(exception);

        // Assert
        Assert.Contains("could not be reached", diagnosis, StringComparison.Ordinal);
        Assert.Contains("refused rather than answered", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>
    /// No diagnosis carries anything the server said. What a read of this table meets is a message naming the
    /// database, the role, or the table, and one of those repeated into a request log is a deployment describing
    /// itself to whoever reads it.
    /// </summary>
    [Theory]
    [InlineData(PostgresErrorCodes.InvalidCatalogName)]
    [InlineData(PostgresErrorCodes.UndefinedTable)]
    [InlineData(PostgresErrorCodes.InvalidPassword)]
    [InlineData(PostgresErrorCodes.InvalidAuthorizationSpecification)]
    [InlineData(PostgresErrorCodes.InsufficientPrivilege)]
    [InlineData(PostgresErrorCodes.SyntaxError)]
    public void DiagnoseWhileReading_AnyRecognizedOrUnrecognizedState_CarriesNothingTheServerSaid(string sqlState)
    {
        // Arrange
        var exception = new PostgresException(
            messageText: "permission denied for table settings_accounts of role \"mailfathom\" at host db.internal",
            severity: "FATAL",
            invariantSeverity: "FATAL",
            sqlState: sqlState);

        // Act
        var diagnosis = OwnerSettingsReadFailures.DiagnoseWhileReading(exception);

        // Assert
        Assert.DoesNotContain("db.internal", diagnosis, StringComparison.Ordinal);
        Assert.DoesNotContain("mailfathom\"", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>
    /// A bound that expired before the connection was open is the opposite diagnosis from one that expired on the
    /// statement: nothing was sent, so the server was never reached — and a pool with nothing left to hand out
    /// arrives in exactly the same shape.
    /// </summary>
    [Fact]
    public void DiagnoseWhileConnecting_TimeoutExpired_ReportsTheDatabaseAsUnreached()
    {
        // Arrange
        var exception = new NpgsqlException("the connection pool has been exhausted", new TimeoutException());

        // Act
        var diagnosis = OwnerSettingsReadFailures.DiagnoseWhileConnecting(exception);

        // Assert
        Assert.Contains("could not be reached", diagnosis, StringComparison.Ordinal);
        Assert.DoesNotContain("Persistence:CommandTimeoutSeconds", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>
    /// A state the server answered the connection attempt with is diagnosed exactly as it is on the statement,
    /// because the correction is the same one: the credential, the authorization rules, the database that is not
    /// there.
    /// </summary>
    [Theory]
    [InlineData(PostgresErrorCodes.InvalidCatalogName, "no database of the configured name")]
    [InlineData(PostgresErrorCodes.InvalidPassword, "Persistence secret block")]
    [InlineData(PostgresErrorCodes.InvalidAuthorizationSpecification, "admits no connection")]
    public void DiagnoseWhileConnecting_AStateTheServerAnswered_SaysWhatTheStatementWould(string sqlState, string expected)
    {
        // Arrange
        var exception = ProviderFailure(sqlState);

        // Act
        var diagnosis = OwnerSettingsReadFailures.DiagnoseWhileConnecting(exception);

        // Assert
        Assert.Contains(expected, diagnosis, StringComparison.Ordinal);
        Assert.Equal(OwnerSettingsReadFailures.DiagnoseWhileReading(exception), diagnosis);
    }

    private static PostgresException ProviderFailure(string sqlState) =>
        new(
            messageText: "the server said something",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: sqlState);
}

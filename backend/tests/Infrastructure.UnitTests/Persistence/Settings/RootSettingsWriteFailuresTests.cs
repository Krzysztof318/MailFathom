// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Settings;
using Npgsql;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Settings;

/// <summary>
/// Covers what a refused write of the persisted configuration tells the operator. The statement needs a database, but
/// which of seven places it sends somebody to is a decision — and the two that matter most are the two the read never
/// meets: a role granted only <c>SELECT</c>, which serves every start and refuses every write, and a timeout, which is
/// the one failure here that cannot say the row stood still.
/// </summary>
public sealed class RootSettingsWriteFailuresTests
{
    /// <summary>The commonest failure on a working deployment, and the reason this diagnosis is not the read's.</summary>
    [Fact]
    public void Diagnose_PrivilegeRefused_SendsTheOperatorToTheUpdateGrant()
    {
        // Arrange
        var exception = ProviderFailure(PostgresErrorCodes.InsufficientPrivilege);

        // Act
        var diagnosis = RootSettingsWriteFailures.Diagnose(exception);

        // Assert
        Assert.Contains("UPDATE", diagnosis, StringComparison.Ordinal);
        Assert.Contains("settings_root", diagnosis, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be reached", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>A standby or a read-only session is a connection pointed at the wrong server rather than a grant.</summary>
    [Fact]
    public void Diagnose_ReadOnlySession_SendsTheOperatorToTheConnection()
    {
        // Arrange
        var exception = ProviderFailure(PostgresErrorCodes.ReadOnlySqlTransaction);

        // Act
        var diagnosis = RootSettingsWriteFailures.Diagnose(exception);

        // Assert
        Assert.Contains("standby", diagnosis, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be reached", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>A rejected password is the secret block's, which keeps the network out of the search.</summary>
    [Fact]
    public void Diagnose_PasswordRefused_SendsTheOperatorToTheSecretBlock()
    {
        // Arrange
        var exception = ProviderFailure(PostgresErrorCodes.InvalidPassword);

        // Act
        var diagnosis = RootSettingsWriteFailures.Diagnose(exception);

        // Assert
        Assert.Contains("Persistence secret block", diagnosis, StringComparison.Ordinal);
        Assert.Contains("nothing was written", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>Refused connections, missing databases, and missing tables each name their own correction.</summary>
    [Theory]
    [InlineData(PostgresErrorCodes.InvalidCatalogName, "no database of the configured name")]
    [InlineData(PostgresErrorCodes.UndefinedTable, "Apply the migrations")]
    [InlineData(PostgresErrorCodes.InvalidAuthorizationSpecification, "admits no connection")]
    public void Diagnose_AServerStateWithItsOwnCorrection_NamesIt(string sqlState, string correction)
    {
        // Arrange
        var exception = ProviderFailure(sqlState);

        // Act
        var diagnosis = RootSettingsWriteFailures.Diagnose(exception);

        // Assert
        Assert.Contains(correction, diagnosis, StringComparison.Ordinal);
        Assert.Contains("nothing was written", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one arm that may not claim the row stood still: the server accepted the statement and stopped answering, so
    /// the operator is sent to read the version rather than told an outcome this side cannot know.
    /// </summary>
    [Fact]
    public void Diagnose_CommandTimeoutExpired_DoesNotClaimTheRowStoodStill()
    {
        // Arrange
        var exception = new NpgsqlException("exception while writing to stream", new TimeoutException());

        // Act
        var diagnosis = RootSettingsWriteFailures.Diagnose(exception);

        // Assert
        Assert.Contains("not known", diagnosis, StringComparison.Ordinal);
        Assert.Contains("Persistence:CommandTimeoutSeconds", diagnosis, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing was written", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>An unrecognized state is a database that could not be reached, and it says nothing it cannot support.</summary>
    [Fact]
    public void Diagnose_UnrecognizedProviderState_ReportsTheDatabaseAsUnreached()
    {
        // Arrange
        var exception = ProviderFailure(PostgresErrorCodes.ConnectionException);

        // Act
        var diagnosis = RootSettingsWriteFailures.Diagnose(exception);

        // Assert
        Assert.Contains("could not be reached", diagnosis, StringComparison.Ordinal);
        Assert.Contains("safe to attempt again", diagnosis, StringComparison.Ordinal);
    }

    private static PostgresException ProviderFailure(string sqlState) =>
        new(
            messageText: "the server said something",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: sqlState);
}

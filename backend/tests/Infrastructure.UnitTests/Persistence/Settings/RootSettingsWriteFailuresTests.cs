// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Settings;
using Npgsql;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Settings;

/// <summary>
/// Covers what a refused write of the persisted configuration tells the operator. The statement needs a database, but
/// which place it sends somebody to is a decision — and what matters most is the pair the read never meets: a role
/// granted only <c>SELECT</c>, which serves every start and refuses every write, and the two endings that cannot say
/// the row stood still, because by then the statement had already been sent.
/// </summary>
public sealed class RootSettingsWriteFailuresTests
{
    /// <summary>What the server said, which no diagnosis may repeat: a provider sentence can name the database.</summary>
    private const string ServerSentence = "the server said something";

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

    /// <summary>
    /// A state no arm names is still a server that answered, so the diagnosis names the state and leaves the operator
    /// reading the statement rather than the network. <c>22P05</c> is the worked example — a value the column cannot
    /// hold — and a retry of it would fail exactly as the first attempt did, which is why the sentence about retrying
    /// belongs to the unreachable case alone.
    /// </summary>
    /// <param name="sqlState">A state the arms above do not name.</param>
    [Theory]
    [InlineData(PostgresErrorCodes.UntranslatableCharacter)]
    [InlineData(PostgresErrorCodes.DiskFull)]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    public void Diagnose_AStateNoArmNames_ReportsAServerThatAnsweredAndNamesTheState(string sqlState)
    {
        // Arrange
        var exception = ProviderFailure(sqlState);

        // Act
        var diagnosis = RootSettingsWriteFailures.Diagnose(exception);

        // Assert
        Assert.Contains(sqlState, diagnosis, StringComparison.Ordinal);
        Assert.Contains("reached and answered", diagnosis, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be reached", diagnosis, StringComparison.Ordinal);
        Assert.DoesNotContain("safe to attempt again", diagnosis, StringComparison.Ordinal);

        // The arm names the state and leaves the provider's own sentence in the inner exception, which is what keeps a
        // server message that can name the database out of what an operator reads. Observed rather than only stated.
        Assert.DoesNotContain(ServerSentence, diagnosis, StringComparison.Ordinal);
    }

    /// <summary>
    /// A connection lost while the statement was in flight carries no state, because the server never got to answer
    /// one — and it cannot claim the row stood still either: the statement had been sent, and the server may have
    /// applied and committed it before the socket died. It is settled by reading the version, exactly as a timeout is.
    /// </summary>
    [Fact]
    public void Diagnose_AConnectionLostAfterTheStatementWasSent_DoesNotClaimTheRowStoodStill()
    {
        // Arrange
        var exception = new NpgsqlException("the connection was reset", new IOException("the socket died"));

        // Act
        var diagnosis = RootSettingsWriteFailures.Diagnose(exception);

        // Assert
        Assert.Contains("not known from here", diagnosis, StringComparison.Ordinal);
        Assert.Contains("Read the version now in force", diagnosis, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing was written", diagnosis, StringComparison.Ordinal);
        Assert.DoesNotContain("safe to attempt again", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>
    /// The driver reports a connect timeout and a command timeout as the same shape, and the two are opposite answers.
    /// A failure raised before the statement was issued cannot leave the commit undecided: nothing was sent, so the row
    /// certainly stood still, and what expired was reaching the database rather than a statement it was holding.
    /// </summary>
    [Fact]
    public void DiagnoseWhileConnecting_ATimeoutBeforeTheStatement_ReportsTheDatabaseAsUnreached()
    {
        // Arrange
        var exception = new NpgsqlException("connecting", new TimeoutException("the attempt expired"));

        // Act
        var diagnosis = RootSettingsWriteFailures.DiagnoseWhileConnecting(exception);

        // Assert
        Assert.Contains("could not be reached", diagnosis, StringComparison.Ordinal);
        Assert.Contains("safe to attempt again", diagnosis, StringComparison.Ordinal);
        Assert.DoesNotContain("Persistence:CommandTimeoutSeconds", diagnosis, StringComparison.Ordinal);
        Assert.DoesNotContain("not known", diagnosis, StringComparison.Ordinal);
    }

    /// <summary>
    /// A state the server answers a connection attempt with is the same operator's correction whichever stage met it,
    /// which is why the two entry points share those arms rather than each carrying its own.
    /// </summary>
    /// <param name="sqlState">A state a server answers a connection attempt with.</param>
    [Theory]
    [InlineData(PostgresErrorCodes.InvalidPassword)]
    [InlineData(PostgresErrorCodes.InvalidCatalogName)]
    [InlineData(PostgresErrorCodes.InvalidAuthorizationSpecification)]
    public void DiagnoseWhileConnecting_AStateTheServerAnswered_IsDiagnosedAsItIsOnTheStatement(string sqlState)
    {
        // Arrange
        var exception = ProviderFailure(sqlState);

        // Act & Assert
        Assert.Equal(
            RootSettingsWriteFailures.Diagnose(exception),
            RootSettingsWriteFailures.DiagnoseWhileConnecting(exception));
    }

    private static PostgresException ProviderFailure(string sqlState) =>
        new(
            messageText: ServerSentence,
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: sqlState);
}

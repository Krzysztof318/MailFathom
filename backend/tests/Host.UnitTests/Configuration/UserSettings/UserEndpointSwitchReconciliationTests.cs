// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Policies;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.Infrastructure.Persistence.Users.EndpointSwitches;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>
/// Covers deriving again the endpoint switches of a user whose row a build that does not know settings policies wrote,
/// which is what keeps a rolling upgrade from leaving somebody served on an endpoint a policy closed.
/// </summary>
public sealed class UserEndpointSwitchReconciliationTests
{
    private const string ServedEverywhere = """{"Language":"English"}""";
    private const string ForcingOffTheClient = """{"Users":{"Forced":{"EndpointAccess":{"ClientEndpoint":false}}}}""";
    private const string ForcingOffMcp = """{"Users":{"Forced":{"EndpointAccess":{"McpEndpoint":false}}}}""";
    private const string NotAPolicy = """{"Users":{"Forced":{"Dialect":"Silesian"}}}""";

    private static readonly UserEndpointAccess ClientOff = new(McpEndpoint: true, ClientEndpoint: false);
    private static readonly UserEndpointAccess McpOff = new(McpEndpoint: false, ClientEndpoint: true);

    /// <summary>
    /// The older build put the record's own switches on the row, and the record states none, so what comes back is
    /// the policy's answer rather than the document's.
    /// </summary>
    [Fact]
    public async Task ReconcileAsync_ARecordABuildThatDoesNotKnowPoliciesWrote_DerivesItsSwitchesFromTheEffectiveRecord()
    {
        // Arrange
        var rows = new InMemoryUserEndpointSwitchRows { DeploymentPolicy = ForcingOffTheClient };
        var user = User(1);
        rows.Hold(user, ServedEverywhere, ClientOff);
        rows.WriteAsAnOlderBuild(user, ServedEverywhere, UserEndpointAccess.Everywhere);

        // Act
        await Reconciliation(rows).ReconcileAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClientOff, rows.SwitchesOf(user));
        Assert.True(rows.IsStampedAsCurrent(user));
    }

    /// <summary>
    /// A move by the older build changes which policy governs the user and nothing else on the row, so the switches
    /// it left are the ones the organization they left resolved.
    /// </summary>
    [Fact]
    public async Task ReconcileAsync_AUserABuildThatDoesNotKnowPoliciesMoved_DerivesTheirSwitchesUnderTheOrganizationTheyStandIn()
    {
        // Arrange
        var rows = new InMemoryUserEndpointSwitchRows();
        rows.GovernedBy(StatedPolicies.Organization, ForcingOffMcp);
        var user = User(1);
        rows.Hold(user, ServedEverywhere, UserEndpointAccess.Everywhere);
        rows.MoveAsAnOlderBuild(user, StatedPolicies.Organization);

        // Act
        await Reconciliation(rows).ReconcileAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(McpOff, rows.SwitchesOf(user));
        Assert.True(rows.IsStampedAsCurrent(user));
    }

    /// <summary>
    /// The row disagrees with the policy on purpose here, which no row this build wrote does: it is what shows the
    /// reconciliation reaches a row by its stamp and by nothing it could read off the switches.
    /// </summary>
    [Fact]
    public async Task ReconcileAsync_ARowWhoseStampMatchesIt_LeavesItAlone()
    {
        // Arrange
        var rows = new InMemoryUserEndpointSwitchRows { DeploymentPolicy = ForcingOffTheClient };
        var user = User(1);
        rows.Hold(user, ServedEverywhere, UserEndpointAccess.Everywhere);

        // Act
        await Reconciliation(rows).ReconcileAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(UserEndpointAccess.Everywhere, rows.SwitchesOf(user));
    }

    /// <summary>
    /// The policies a row is derived under are the ones in force inside the write, so a policy that commits after
    /// the row was found and before it is written is the one the row ends up holding.
    /// </summary>
    [Fact]
    public async Task ReconcileAsync_APolicyWriteLandingBetweenTheReadingAndTheWrite_LeavesTheRowHoldingTheValueOfTheNewPolicy()
    {
        // Arrange
        var rows = new InMemoryUserEndpointSwitchRows { DeploymentPolicy = ForcingOffTheClient };
        var user = User(1);
        rows.Hold(user, ServedEverywhere, ClientOff);
        rows.WriteAsAnOlderBuild(user, ServedEverywhere, UserEndpointAccess.Everywhere);
        rows.AfterReading = () => rows.DeploymentPolicy = ForcingOffMcp;

        // Act
        await Reconciliation(rows).ReconcileAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(McpOff, rows.SwitchesOf(user));
    }

    /// <summary>
    /// Two replicas read the same trailing row, and the one that reaches it second finds it stamped and derives
    /// nothing. The policy moves here only so that a second derivation would show on the row.
    /// </summary>
    [Fact]
    public async Task ReconcileAsync_AnotherReplicaDerivingTheRowFirst_WritesNothingOverIt()
    {
        // Arrange
        var rows = new InMemoryUserEndpointSwitchRows { DeploymentPolicy = ForcingOffTheClient };
        var user = User(1);
        rows.Hold(user, ServedEverywhere, ClientOff);
        rows.WriteAsAnOlderBuild(user, ServedEverywhere, UserEndpointAccess.Everywhere);
        rows.AfterReading = () =>
        {
            rows.Hold(user, ServedEverywhere, ClientOff);
            rows.DeploymentPolicy = ForcingOffMcp;
        };

        // Act
        await Reconciliation(rows).ReconcileAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClientOff, rows.SwitchesOf(user));
    }

    /// <summary>
    /// A policy this build does not read resolves nothing and forces nothing it can read, so the row keeps its
    /// switches and its stamp, and a build that reads the policy still finds it.
    /// </summary>
    [Fact]
    public async Task ReconcileAsync_ARowGovernedByAPolicyThisBuildDoesNotRead_LeavesItTrailing()
    {
        // Arrange
        var rows = new InMemoryUserEndpointSwitchRows { DeploymentPolicy = NotAPolicy };
        var user = User(1);
        rows.Hold(user, ServedEverywhere, ClientOff);
        rows.WriteAsAnOlderBuild(user, ServedEverywhere, UserEndpointAccess.Everywhere);

        // Act
        await Reconciliation(rows).ReconcileAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(UserEndpointAccess.Everywhere, rows.SwitchesOf(user));
        Assert.False(rows.IsStampedAsCurrent(user));
    }

    /// <summary>A record past the size MailFathom binds one from states no switch, and a force reaches its row all the same.</summary>
    [Fact]
    public async Task ReconcileAsync_ARecordLeftInTheDatabaseForItsSize_WritesWhatThePolicyForcesOverWhatItsRowHolds()
    {
        // Arrange
        var rows = new InMemoryUserEndpointSwitchRows { DeploymentPolicy = ForcingOffTheClient };
        var user = User(1);
        rows.Hold(user, ServedEverywhere, ClientOff);
        rows.WriteAsAnOlderBuild(user, json: null, McpOff);

        // Act
        await Reconciliation(rows).ReconcileAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new UserEndpointAccess(McpEndpoint: false, ClientEndpoint: false), rows.SwitchesOf(user));
        Assert.True(rows.IsStampedAsCurrent(user));
    }

    /// <summary>One interval reads a bounded number of rows, and the ones it did not reach are reached by the intervals after it.</summary>
    [Fact]
    public async Task ReconcileAsync_MoreRowsTrailingThanOneReadingTakes_ReachesTheRestOnLaterIntervals()
    {
        // Arrange
        var rows = new InMemoryUserEndpointSwitchRows { DeploymentPolicy = ForcingOffTheClient };
        var users = Enumerable.Range(1, (2 * UserEndpointSwitchReconciliation.MaximumUsersPerReading) + 1).Select(User).ToArray();

        foreach (var user in users)
        {
            rows.Hold(user, ServedEverywhere, ClientOff);
            rows.WriteAsAnOlderBuild(user, ServedEverywhere, UserEndpointAccess.Everywhere);
        }

        var reconciliation = Reconciliation(rows);

        // Act
        await reconciliation.ReconcileAsync(TestContext.Current.CancellationToken);
        var afterOneInterval = users.Count(rows.IsStampedAsCurrent);
        await reconciliation.ReconcileAsync(TestContext.Current.CancellationToken);
        await reconciliation.ReconcileAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(UserEndpointSwitchReconciliation.MaximumUsersPerReading, afterOneInterval);
        Assert.All(rows.Readings, limit => Assert.Equal(UserEndpointSwitchReconciliation.MaximumUsersPerReading, limit));
        Assert.All(users, user => Assert.Equal(ClientOff, rows.SwitchesOf(user)));
    }

    /// <summary>
    /// Rows that cannot be stamped stay trailing, and they sort first here. A reading that started from the first
    /// row every time would read the same full page of them on every interval and never reach the row behind them.
    /// </summary>
    [Fact]
    public async Task ReconcileAsync_AFullReadingOfRowsThatCannotBeStamped_ReachesTheRowBehindThemOnTheNextInterval()
    {
        // Arrange
        var rows = new InMemoryUserEndpointSwitchRows();
        rows.GovernedBy(StatedPolicies.Organization, NotAPolicy);
        var behind = User(UserEndpointSwitchReconciliation.MaximumUsersPerReading + 1);

        foreach (var user in Enumerable.Range(1, UserEndpointSwitchReconciliation.MaximumUsersPerReading).Select(User))
        {
            rows.Hold(user, ServedEverywhere, UserEndpointAccess.Everywhere, StatedPolicies.Organization);
            rows.WriteAsAnOlderBuild(user, ServedEverywhere, UserEndpointAccess.Everywhere);
        }

        rows.Hold(behind, ServedEverywhere, UserEndpointAccess.Everywhere);
        rows.WriteAsAnOlderBuild(behind, ServedEverywhere, UserEndpointAccess.Everywhere);
        var reconciliation = Reconciliation(rows);

        // Act
        await reconciliation.ReconcileAsync(TestContext.Current.CancellationToken);
        var reachedOnTheFirstInterval = rows.IsStampedAsCurrent(behind);
        await reconciliation.ReconcileAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(reachedOnTheFirstInterval);
        Assert.True(rows.IsStampedAsCurrent(behind));
    }

    /// <summary>The rows are read in the same order every interval, so one that cannot be derived must not hold back the rest.</summary>
    [Fact]
    public async Task ReconcileAsync_OneUserWhoseSwitchesCannotBeDerived_DerivesTheUsersBehindThemAndReportsIt()
    {
        // Arrange
        var failing = User(1);
        var behind = User(2);
        var rows = Substitute.For<IUserEndpointSwitchStore>();
        rows.ReadTrailingAsync(Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([failing, behind]);
        rows.DeriveAgainAsync(failing, Arg.Any<Func<GovernedUserRecord, UserEndpointAccess?>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The row was refused."));
        var logger = new RecordingLogger<UserEndpointSwitchReconciliation>();

        // Act
        await Reconciliation(rows, logger).ReconcileAsync(TestContext.Current.CancellationToken);

        // Assert
        await rows.Received(1).DeriveAgainAsync(
            behind,
            Arg.Any<Func<GovernedUserRecord, UserEndpointAccess?>>(),
            Arg.Any<CancellationToken>());
        Assert.Contains(failing.Value.ToString(), Assert.Single(logger.Messages), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReconcileAsync_TheCallerCancelling_PropagatesTheCancellation()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var rows = Substitute.For<IUserEndpointSwitchStore>();
        rows.ReadTrailingAsync(Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([User(1)]);
        rows.DeriveAgainAsync(Arg.Any<UserId>(), Arg.Any<Func<GovernedUserRecord, UserEndpointAccess?>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        // Act, Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Reconciliation(rows, new RecordingLogger<UserEndpointSwitchReconciliation>()).ReconcileAsync(cancellation.Token));
    }

    private static UserEndpointSwitchReconciliation Reconciliation(IUserEndpointSwitchStore rows) =>
        Reconciliation(rows, new RecordingLogger<UserEndpointSwitchReconciliation>());

    private static UserEndpointSwitchReconciliation Reconciliation(
        IUserEndpointSwitchStore rows,
        RecordingLogger<UserEndpointSwitchReconciliation> logger) =>
        new(
            rows,
            new UserRecordPolicyJudge(new UserAccountDocumentBinder(
                new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
                new FakeTimeProvider(),
                Options.Create(new SensitiveContentOptions()))),
            logger);

    private static UserId User(int ordinal) => UserId.Create(new Guid($"0199a0c0-0000-7000-8000-{ordinal:D12}"));
}

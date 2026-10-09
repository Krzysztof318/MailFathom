// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>Covers that a user an erasure is deciding about reaches no mailbox, although their assignment rows still exist.</summary>
public sealed class WithholdingMailAccountAssignmentsTests
{
    private static readonly MailAccountId Shared = MailAccountId.Create("0199a0c0-0000-7000-8000-00000000000a");

    /// <summary>While the erasure runs, the withheld user is assigned nothing and is nobody an account reaches.</summary>
    [Fact]
    public async Task ReadAsync_AUserBeingErased_AnswersThemWithNothingWhileOthersStay()
    {
        // Arrange
        var servedUsers = ResolvedServedUsers.Serving(
            new ServedUser(SyntheticUser.Deployment, "erased", []),
            new ServedUser(SyntheticUser.Another, "kept", []));
        var assignments = new WithholdingMailAccountAssignments(
            new StubMailAccountAssignments()
                .Assigning(SyntheticUser.Deployment, Shared)
                .Assigning(SyntheticUser.Another, Shared),
            servedUsers);
        using var withheld = servedUsers.Withhold(SyntheticUser.Deployment);

        // Act
        var assignedToErased = await assignments.ReadAccountsAssignedToAsync(
            SyntheticUser.Deployment,
            TestContext.Current.CancellationToken);
        var reaching = await assignments.ReadUsersAssignedToAsync(Shared, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(assignedToErased);
        Assert.Equal([SyntheticUser.Another], reaching);
    }

    /// <summary>
    /// A user this replica's roster does not serve is still assigned in the relation, so an erasure deciding about them
    /// takes their mailboxes away here as well rather than passing their rows through.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AUserBeingErasedWhomThisRosterDoesNotServe_AnswersThemWithNothing()
    {
        // Arrange
        var servedUsers = ResolvedServedUsers.Serving(new ServedUser(SyntheticUser.Another, "kept", []));
        var assignments = new WithholdingMailAccountAssignments(
            new StubMailAccountAssignments()
                .Assigning(SyntheticUser.Deployment, Shared)
                .Assigning(SyntheticUser.Another, Shared),
            servedUsers);
        using var withheld = servedUsers.Withhold(SyntheticUser.Deployment);

        // Act
        var assignedToErased = await assignments.ReadAccountsAssignedToAsync(
            SyntheticUser.Deployment,
            TestContext.Current.CancellationToken);
        var reaching = await assignments.ReadUsersAssignedToAsync(Shared, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(assignedToErased);
        Assert.Equal([SyntheticUser.Another], reaching);
    }

    /// <summary>The erasure of a user this roster does not serve ends, and their mailboxes are theirs again.</summary>
    [Fact]
    public async Task ReadAsync_AnErasureOfAUserThisRosterDoesNotServeRefused_AnswersTheUserAsBefore()
    {
        // Arrange
        var servedUsers = ResolvedServedUsers.Serving(new ServedUser(SyntheticUser.Another, "kept", []));
        var assignments = new WithholdingMailAccountAssignments(
            new StubMailAccountAssignments().Assigning(SyntheticUser.Deployment, Shared),
            servedUsers);

        // Act
        servedUsers.Withhold(SyntheticUser.Deployment).Dispose();
        var assigned = await assignments.ReadAccountsAssignedToAsync(
            SyntheticUser.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([Shared], assigned);
    }

    /// <summary>A refused erasure puts the user back, and with them every mailbox they were assigned.</summary>
    [Fact]
    public async Task ReadAsync_AnErasureRefused_AnswersTheUserAsBefore()
    {
        // Arrange
        var servedUsers = ResolvedServedUsers.Serving(new ServedUser(SyntheticUser.Deployment, "kept", []));
        var assignments = new WithholdingMailAccountAssignments(
            new StubMailAccountAssignments().Assigning(SyntheticUser.Deployment, Shared),
            servedUsers);

        // Act
        servedUsers.Withhold(SyntheticUser.Deployment).Dispose();
        var assigned = await assignments.ReadAccountsAssignedToAsync(
            SyntheticUser.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([Shared], assigned);
    }
}

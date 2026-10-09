// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the assignment relation every suite about who reaches which mailbox arranges through.</summary>
/// <remarks>
/// The relation is read from both ends, and a double that answered one end from a separate arrangement could
/// contradict itself: a mailbox two people share would be shared in one direction and not the other, which is the one
/// claim the tests borrowing this helper exist to make.
/// </remarks>
public sealed class StubMailAccountAssignmentsTests
{
    [Fact]
    public async Task ReadAccountsAssignedToAsync_AUserAssignedNothing_AreNone()
    {
        // Arrange
        var assignments = new StubMailAccountAssignments();

        // Act, Assert
        Assert.Empty(await assignments.ReadAccountsAssignedToAsync(SyntheticUser.Deployment, TestContext.Current.CancellationToken));
        Assert.Empty(await assignments.ReadUsersAssignedToAsync(SyntheticMailAccount.Deployment, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadAccountsAssignedToAsync_AUserAssignedTwoMailboxes_AreBothOfThem()
    {
        // Arrange
        var assignments = new StubMailAccountAssignments()
            .Assigning(SyntheticUser.Deployment, SyntheticMailAccount.Deployment, SyntheticMailAccount.Another);

        // Act
        var assigned = await assignments.ReadAccountsAssignedToAsync(SyntheticUser.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([SyntheticMailAccount.Deployment, SyntheticMailAccount.Another], assigned);
    }

    /// <summary>One mailbox assigned to two people is the same mailbox for both, read from either end.</summary>
    [Fact]
    public async Task ReadUsersAssignedToAsync_AMailboxTwoPeopleShare_AreBothOfThem()
    {
        // Arrange
        var assignments = new StubMailAccountAssignments()
            .Assigning(SyntheticUser.Deployment, SyntheticMailAccount.Deployment)
            .Assigning(SyntheticUser.Another, SyntheticMailAccount.Deployment);

        // Act
        var readers = await assignments.ReadUsersAssignedToAsync(SyntheticMailAccount.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([SyntheticUser.Deployment, SyntheticUser.Another], readers);
        Assert.Equal([SyntheticMailAccount.Deployment], await assignments.ReadAccountsAssignedToAsync(SyntheticUser.Another, TestContext.Current.CancellationToken));
    }

    /// <summary>A user assigned one mailbox reaches no other, which is what every narrowing test rests on.</summary>
    [Fact]
    public async Task ReadAccountsAssignedToAsync_AnotherUsersMailbox_IsNotAnsweredWith()
    {
        // Arrange
        var assignments = new StubMailAccountAssignments()
            .Assigning(SyntheticUser.Deployment, SyntheticMailAccount.Deployment)
            .Assigning(SyntheticUser.Another, SyntheticMailAccount.Another);

        // Act
        var assigned = await assignments.ReadAccountsAssignedToAsync(SyntheticUser.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(SyntheticMailAccount.Another, assigned);
    }
}

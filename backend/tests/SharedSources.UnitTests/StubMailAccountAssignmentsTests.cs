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
    public void AccountsAssignedTo_AUserAssignedNothing_AreNone()
    {
        // Arrange
        var assignments = new StubMailAccountAssignments();

        // Act, Assert
        Assert.Empty(assignments.AccountsAssignedTo(SyntheticMailUser.Deployment));
        Assert.Empty(assignments.UsersAssignedTo(SyntheticMailAccount.Deployment));
    }

    [Fact]
    public void AccountsAssignedTo_AUserAssignedTwoMailboxes_AreBothOfThem()
    {
        // Arrange
        var assignments = new StubMailAccountAssignments()
            .Assigning(SyntheticMailUser.Deployment, SyntheticMailAccount.Deployment, SyntheticMailAccount.Another);

        // Act
        var assigned = assignments.AccountsAssignedTo(SyntheticMailUser.Deployment);

        // Assert
        Assert.Equal([SyntheticMailAccount.Deployment, SyntheticMailAccount.Another], assigned);
    }

    /// <summary>One mailbox assigned to two people is the same mailbox for both, read from either end.</summary>
    [Fact]
    public void UsersAssignedTo_AMailboxTwoPeopleShare_AreBothOfThem()
    {
        // Arrange
        var assignments = new StubMailAccountAssignments()
            .Assigning(SyntheticMailUser.Deployment, SyntheticMailAccount.Deployment)
            .Assigning(SyntheticMailUser.Another, SyntheticMailAccount.Deployment);

        // Act
        var readers = assignments.UsersAssignedTo(SyntheticMailAccount.Deployment);

        // Assert
        Assert.Equal([SyntheticMailUser.Deployment, SyntheticMailUser.Another], readers);
        Assert.Equal([SyntheticMailAccount.Deployment], assignments.AccountsAssignedTo(SyntheticMailUser.Another));
    }

    /// <summary>A user assigned one mailbox reaches no other, which is what every narrowing test rests on.</summary>
    [Fact]
    public void AccountsAssignedTo_AnotherUsersMailbox_IsNotAnsweredWith()
    {
        // Arrange
        var assignments = new StubMailAccountAssignments()
            .Assigning(SyntheticMailUser.Deployment, SyntheticMailAccount.Deployment)
            .Assigning(SyntheticMailUser.Another, SyntheticMailAccount.Another);

        // Act
        var assigned = assignments.AccountsAssignedTo(SyntheticMailUser.Deployment);

        // Assert
        Assert.DoesNotContain(SyntheticMailAccount.Another, assigned);
    }
}

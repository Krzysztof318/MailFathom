// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Enrichment;
using MailFathom.Application.Tasks;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Tasks;

/// <summary>
/// Covers the seam between a derivation of mail and somebody's task list: whose list a reading lands on, under which
/// origin it lands, and that a message asking for nothing reaches nobody.
/// </summary>
public sealed class MailDerivedTaskProposalsTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly StoredEmailId Message =
        StoredEmailId.Create(new Guid("0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90"));

    private static readonly DateTimeOffset Stamped = new(2026, 9, 20, 9, 0, 0, TimeSpan.Zero);

    /// <summary>What mail suggested is offered rather than owed, which is the whole of what the origin says.</summary>
    [Fact]
    public async Task ProposeAsync_AReadingOfOneMessage_WritesItAsAProposalCitingThatMessage()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        var proposals = Compose(store, assignedTo: [SyntheticMailUser.Deployment]);

        // Act
        var written = await proposals.ProposeAsync(
            Account,
            Message,
            [EmailTaskProposal.Create("Answer the supplier", new DateOnly(2026, 9, 21))],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, written);
        await store.Received(1).AddAsync(
            Arg.Is<PersonalTask>(task =>
                task!.User == SyntheticMailUser.Deployment
                && task.Title == "Answer the supplier"
                && task.DueOn == new DateOnly(2026, 9, 21)
                && task.Origin == PersonalTaskOrigin.Proposed
                && task.SourceMessage == Message
                && !task.IsCompleted),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A list is one person's, so a mailbox two people share offers the same reading to each of them.</summary>
    [Fact]
    public async Task ProposeAsync_AMailboxTwoPeopleAreAssigned_OffersTheReadingOnEachOfTheirLists()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        var proposals = Compose(
            store,
            assignedTo: [SyntheticMailUser.Deployment, SyntheticMailUser.Another]);

        // Act
        var written = await proposals.ProposeAsync(
            Account,
            Message,
            [EmailTaskProposal.Create("Answer the supplier", dueOn: null)],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, written);
        await store.Received(1).AddAsync(
            Arg.Is<PersonalTask>(task => task!.User == SyntheticMailUser.Deployment),
            Arg.Any<CancellationToken>());
        await store.Received(1).AddAsync(
            Arg.Is<PersonalTask>(task => task!.User == SyntheticMailUser.Another),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A mailbox nobody is assigned proposes to nobody rather than to everybody.</summary>
    [Fact]
    public async Task ProposeAsync_AMailboxAssignedToNobody_WritesNothing()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        var proposals = Compose(store, assignedTo: []);

        // Act
        var written = await proposals.ProposeAsync(
            Account,
            Message,
            [EmailTaskProposal.Create("Answer the supplier", dueOn: null)],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, written);
        await store.DidNotReceiveWithAnyArgs().AddAsync(Arg.Any<PersonalTask>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Most mail asks for nothing, and reading it must not cost a write.</summary>
    [Fact]
    public async Task ProposeAsync_AMessageThatAskedForNothing_ReachesNeitherTheRelationNorTheStore()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        var proposals = Compose(store, assignedTo: [SyntheticMailUser.Deployment]);

        // Act
        var written = await proposals.ProposeAsync(
            Account,
            Message,
            [],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, written);
        await store.DidNotReceiveWithAnyArgs().AddAsync(Arg.Any<PersonalTask>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Two readings of one message are two rows, each addressed under an identity of its own.</summary>
    [Fact]
    public async Task ProposeAsync_SeveralReadingsOfOneMessage_AddressesEachUnderItsOwnIdentity()
    {
        // Arrange
        var written = new List<PersonalTask>();
        var store = Substitute.For<IPersonalTaskStore>();
        store
            .AddAsync(Arg.Any<PersonalTask>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                written.Add(call.ArgAt<PersonalTask>(0));

                return Task.CompletedTask;
            });

        var proposals = Compose(store, assignedTo: [SyntheticMailUser.Deployment]);

        // Act
        await proposals.ProposeAsync(
            Account,
            Message,
            [
                EmailTaskProposal.Create("Answer the supplier", dueOn: null),
                EmailTaskProposal.Create("Countersign the quotation", dueOn: null),
            ],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, written.Count);
        Assert.Distinct(written.Select(task => task.Id));
    }

    /// <summary>
    /// A reading of a message proposes the task and never the reminder: what somebody wants to be told about is
    /// theirs to set once they have accepted it, and a proposal nobody agreed to that announced itself would be mail
    /// raising notifications of its own.
    /// </summary>
    [Fact]
    public async Task ProposeAsync_AProposalCarryingADueDay_AnnouncesNothing()
    {
        // Arrange
        var written = new List<PersonalTask>();
        var store = Substitute.For<IPersonalTaskStore>();
        store
            .AddAsync(Arg.Any<PersonalTask>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                written.Add(call.ArgAt<PersonalTask>(0));

                return Task.CompletedTask;
            });

        var proposals = Compose(store, assignedTo: [SyntheticMailUser.Deployment]);

        // Act
        await proposals.ProposeAsync(
            Account,
            Message,
            [EmailTaskProposal.Create("Answer the supplier", new DateOnly(2026, 9, 30))],
            TestContext.Current.CancellationToken);

        // Assert
        var proposed = Assert.Single(written);

        Assert.Empty(proposed.Reminders);
        Assert.Null(proposed.DueDayOffset);
        Assert.Null(proposed.AnchorsRemindersAt);
    }

    private static MailDerivedTaskProposals Compose(
        IPersonalTaskStore store,
        IReadOnlyList<MailUserId> assignedTo)
    {
        var assignments = new StubMailAccountAssignments();

        foreach (var user in assignedTo)
        {
            assignments.Assigning(user, Account);
        }

        return new MailDerivedTaskProposals(assignments, store, new FakeTimeProvider(Stamped));
    }
}

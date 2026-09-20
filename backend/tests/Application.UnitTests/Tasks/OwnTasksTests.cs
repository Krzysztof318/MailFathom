// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Tasks;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.Tasks;

/// <summary>
/// Covers the use case a person reads and writes their own task list through. What it has to hold is that the person
/// acted on is the one the credential authenticated rather than one a caller could name, that the two origins are read
/// apart and a cursor does not cross between them, that a page is bounded and clamped rather than refused, and that a
/// task somebody else holds answers exactly as one that does not exist.
/// </summary>
public sealed class OwnTasksTests
{
    private static readonly MailUserId User = SyntheticMailUser.Deployment;

    private static readonly MailUserId SomebodyElse = MailUserId.Create(
        new Guid("6d0b6a1c-6f5e-4a7e-9a1a-8d2a3f4b5c60"));

    private static readonly DateTimeOffset Stamped = new(2026, 9, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadPageAsync_APersonWithTasks_AnswersTheirOwnSoonestDueFirstWithTheUndatedLast()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        await KeepAsync(store, User, "undated", dueOn: null);
        await KeepAsync(store, User, "later", new DateOnly(2026, 10, 1));
        await KeepAsync(store, User, "sooner", new DateOnly(2026, 9, 21));
        await KeepAsync(store, SomebodyElse, "theirs", new DateOnly(2026, 9, 20));

        // Act
        var page = await SignedIn(store).ReadPageAsync(
            PersonalTaskOrigin.Asserted,
            null,
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["sooner", "later", "undated"], page!.Tasks.Select(task => task.Title));
    }

    /// <summary>What mail suggested is an offer rather than a commitment, so it is read apart from the list of what is owed.</summary>
    [Fact]
    public async Task ReadPageAsync_TheProposedOrigin_AnswersWhatMailSuggestedAndNothingCommittedTo()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        await KeepAsync(store, User, "committed", new DateOnly(2026, 9, 21));
        await KeepAsync(store, User, "suggested", new DateOnly(2026, 9, 22), PersonalTaskOrigin.Proposed);

        // Act
        var page = await SignedIn(store).ReadPageAsync(
            PersonalTaskOrigin.Proposed,
            null,
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("suggested", Assert.Single(page!.Tasks).Title);
    }

    /// <summary>A panel asks for as much as it can draw, so the useful answer to a number no screen wants is the most this deployment serves.</summary>
    [Theory]
    [InlineData(null, OwnTasks.DefaultPageSize)]
    [InlineData(0, OwnTasks.DefaultPageSize)]
    [InlineData(-5, OwnTasks.DefaultPageSize)]
    [InlineData(1000, OwnTasks.MaximumPageSize)]
    [InlineData(7, 7)]
    public async Task ReadPageAsync_APageSizeAskedFor_IsClampedToWhatThisDeploymentServes(int? asked, int served)
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();

        // Act
        await SignedIn(store).ReadPageAsync(
            PersonalTaskOrigin.Asserted,
            asked,
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(served, store.LastLimit);
    }

    /// <summary>The boundary continues the walk rather than shifting a window, so a task dated while somebody pages neither repeats nor skips a row.</summary>
    [Fact]
    public async Task ReadPageAsync_TheCursorAPreviousPageReturned_ContinuesBeyondIt()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        await KeepAsync(store, User, "sooner", new DateOnly(2026, 9, 21));
        await KeepAsync(store, User, "later", new DateOnly(2026, 10, 1));

        var tasks = SignedIn(store);
        var first = await tasks.ReadPageAsync(
            PersonalTaskOrigin.Asserted,
            1,
            null,
            TestContext.Current.CancellationToken);

        // Act
        var second = await tasks.ReadPageAsync(
            PersonalTaskOrigin.Asserted,
            1,
            first!.NextCursor,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("sooner", Assert.Single(first.Tasks).Title);
        Assert.Equal("later", Assert.Single(second!.Tasks).Title);
    }

    /// <summary>The undated block is a position like any other, so a walk that ended inside it continues through the rest of it.</summary>
    [Fact]
    public async Task ReadPageAsync_ACursorInsideTheUndatedBlock_ContinuesThroughThatBlock()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        await KeepAsync(store, User, "first-undated", dueOn: null);
        await KeepAsync(store, User, "second-undated", dueOn: null);

        var tasks = SignedIn(store);
        var first = await tasks.ReadPageAsync(
            PersonalTaskOrigin.Asserted,
            1,
            null,
            TestContext.Current.CancellationToken);

        // Act
        var second = await tasks.ReadPageAsync(
            PersonalTaskOrigin.Asserted,
            1,
            first!.NextCursor,
            TestContext.Current.CancellationToken);

        // Assert
        var walked = Assert.Single(second!.Tasks);
        Assert.Null(walked.DueOn);
        Assert.NotEqual(Assert.Single(first.Tasks).Id, walked.Id);
    }

    /// <summary>A short page is the end of the list, so a caller stops on the absent cursor rather than on a length comparison.</summary>
    [Fact]
    public async Task ReadPageAsync_APageTheListCouldNotFill_CarriesNoCursor()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        await KeepAsync(store, User, "only", new DateOnly(2026, 9, 21));

        // Act
        var page = await SignedIn(store).ReadPageAsync(
            PersonalTaskOrigin.Asserted,
            10,
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(page!.NextCursor);
    }

    /// <summary>The origin is part of what the cursor was issued for, so a walk of the proposals names no boundary in the commitments.</summary>
    [Fact]
    public async Task ReadPageAsync_ACursorIssuedForTheOtherOrigin_IsRefused()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        await KeepAsync(store, User, "suggested", new DateOnly(2026, 9, 21), PersonalTaskOrigin.Proposed);
        await KeepAsync(store, User, "also-suggested", new DateOnly(2026, 9, 22), PersonalTaskOrigin.Proposed);

        var tasks = SignedIn(store);
        var proposed = await tasks.ReadPageAsync(
            PersonalTaskOrigin.Proposed,
            1,
            null,
            TestContext.Current.CancellationToken);

        // Act
        var committed = await tasks.ReadPageAsync(
            PersonalTaskOrigin.Asserted,
            1,
            proposed!.NextCursor,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(committed);
    }

    /// <summary>The fingerprint carries the user, so a cursor issued to somebody else names no boundary in this caller's own walk.</summary>
    [Fact]
    public async Task ReadPageAsync_ACursorIssuedForAnotherUser_IsRefused()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        await KeepAsync(store, SomebodyElse, "theirs", new DateOnly(2026, 9, 21));
        await KeepAsync(store, SomebodyElse, "theirs-too", new DateOnly(2026, 9, 22));

        var elsewhere = await Signed(store, SomebodyElse).ReadPageAsync(
            PersonalTaskOrigin.Asserted,
            1,
            null,
            TestContext.Current.CancellationToken);

        // Act
        var page = await SignedIn(store).ReadPageAsync(
            PersonalTaskOrigin.Asserted,
            1,
            elsewhere!.NextCursor,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(page);
    }

    /// <summary>A boundary this deployment never issued names no page, and answering it with the first one would be a list silently jumping back to the top.</summary>
    [Fact]
    public async Task ReadPageAsync_ACursorThisDeploymentDidNotIssue_IsRefusedRatherThanIgnored()
    {
        // Act
        var page = await SignedIn(new InMemoryPersonalTaskStore()).ReadPageAsync(
            PersonalTaskOrigin.Asserted,
            10,
            "not-a-cursor-this-deployment-issued",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(page);
    }

    /// <summary>Completed tasks are the screen's decision rather than the list's, so they are served with the outstanding ones.</summary>
    [Fact]
    public async Task ReadPageAsync_ACompletedTask_IsServedWithTheOutstandingOnes()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var done = await KeepAsync(store, User, "done", new DateOnly(2026, 9, 21));
        var tasks = SignedIn(store);
        await tasks.SetCompletionAsync(done.Id, true, TestContext.Current.CancellationToken);

        // Act
        var page = await tasks.ReadPageAsync(
            PersonalTaskOrigin.Asserted,
            10,
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Assert.Single(page!.Tasks).IsCompleted);
    }

    /// <summary>What a person types into their own client is something they owe, so this is not the seam a proposal arrives through.</summary>
    [Fact]
    public async Task RecordAsync_ATaskAPersonTyped_WritesItAssertedForThemAndOutstanding()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var cited = StoredEmailId.Create(new Guid("0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90"));

        // Act
        var written = await SignedIn(store).RecordAsync(
            "  Answer the tender  ",
            new DateOnly(2026, 9, 21),
            cited,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Answer the tender", written.Title);
        Assert.Equal(User, written.User);
        Assert.Equal(PersonalTaskOrigin.Asserted, written.Origin);
        Assert.Equal(cited, written.SourceMessage);
        Assert.False(written.IsCompleted);
        Assert.Equal(written, Assert.Single(store.Held));
    }

    [Fact]
    public async Task ReviseAsync_ATaskOfTheirOwn_WritesTheLineAndTheDayAndLeavesTheRest()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var kept = await KeepAsync(store, User, "draft the reply", new DateOnly(2026, 9, 21), PersonalTaskOrigin.Proposed);

        // Act
        var revised = await SignedIn(store).ReviseAsync(
            kept.Id,
            "draft the reply to the tender",
            dueOn: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("draft the reply to the tender", revised!.Title);
        Assert.Null(revised.DueOn);
        Assert.Equal(PersonalTaskOrigin.Proposed, revised.Origin);
        Assert.Equal(revised, Assert.Single(store.Held));
    }

    /// <summary>The answer is the same one an absent task gets, so nothing here reports whose tasks exist.</summary>
    [Fact]
    public async Task ReviseAsync_ATaskSomebodyElseHolds_AnswersAsOneThatDoesNotExist()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var theirs = await KeepAsync(store, SomebodyElse, "theirs", new DateOnly(2026, 9, 21));

        // Act
        var revised = await SignedIn(store).ReviseAsync(
            theirs.Id,
            "mine now",
            dueOn: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(revised);
        Assert.Equal("theirs", Assert.Single(store.Held).Title);
    }

    /// <summary>Accepting moves the row that already exists, so a commitment keeps the identity it was proposed under.</summary>
    [Fact]
    public async Task AcceptAsync_AProposalOfTheirOwn_MovesItsOriginAndKeepsItsIdentity()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var proposed = await KeepAsync(store, User, "call them back", dueOn: null, PersonalTaskOrigin.Proposed);

        // Act
        var accepted = await SignedIn(store).AcceptAsync(proposed.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(proposed.Id, accepted!.Id);
        Assert.Equal(PersonalTaskOrigin.Asserted, accepted.Origin);
        Assert.Equal(proposed.Id, Assert.Single(store.Held).Id);
    }

    /// <summary>A state a task already stands in is the state the caller asked for, so accepting twice writes once and reports done.</summary>
    [Fact]
    public async Task AcceptAsync_ATaskAlreadyAsserted_IsAnsweredAsDone()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var committed = await KeepAsync(store, User, "already mine", dueOn: null);

        // Act
        var accepted = await SignedIn(store).AcceptAsync(committed.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(PersonalTaskOrigin.Asserted, accepted!.Origin);
    }

    [Fact]
    public async Task SetCompletionAsync_ATaskOfTheirOwn_CompletesItAndPutsItBack()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var kept = await KeepAsync(store, User, "file the return", new DateOnly(2026, 9, 21));
        var tasks = SignedIn(store);

        // Act
        var completed = await tasks.SetCompletionAsync(kept.Id, true, TestContext.Current.CancellationToken);
        var outstanding = await tasks.SetCompletionAsync(kept.Id, false, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(completed!.IsCompleted);
        Assert.False(outstanding!.IsCompleted);
    }

    /// <summary>The identifier is not a capability: the write carries the caller inside it, so somebody else's task is neither moved nor reported.</summary>
    [Fact]
    public async Task SetCompletionAsync_ATaskSomebodyElseHolds_AnswersAsOneThatDoesNotExist()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var theirs = await KeepAsync(store, SomebodyElse, "theirs", dueOn: null);

        // Act
        var changed = await SignedIn(store).SetCompletionAsync(
            theirs.Id,
            true,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(changed);
        Assert.False(Assert.Single(store.Held).IsCompleted);
    }

    /// <summary>Dismissing a proposal is this same erasure, which is why there is no second act for it.</summary>
    [Fact]
    public async Task EraseAsync_AProposalOfTheirOwn_TakesItOutOfTheList()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var proposed = await KeepAsync(store, User, "suggested", dueOn: null, PersonalTaskOrigin.Proposed);

        // Act
        var erased = await SignedIn(store).EraseAsync(proposed.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(erased);
        Assert.Empty(store.Held);
    }

    /// <summary>The act is what the caller wanted either way, so a task that has already gone is answered rather than refused.</summary>
    [Fact]
    public async Task EraseAsync_ATaskThatIsAlreadyGone_ErasesNothingAndDoesNotFail()
    {
        // Act
        var erased = await SignedIn(new InMemoryPersonalTaskStore()).EraseAsync(
            PersonalTaskId.Create(Guid.CreateVersion7(Stamped)),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(erased);
    }

    /// <summary>The list is a person's own working state, so a caller holding no reading grant reaches none of it.</summary>
    [Fact]
    public async Task ReadPageAsync_ACallerWithoutTheReadingGrant_IsRefused()
    {
        // Arrange
        var tasks = new OwnTasks(
            AccessAuthorizations.ForUserGranted(User, MailFathomPermission.MailSend),
            new InMemoryPersonalTaskStore(),
            new FakeTimeProvider(Stamped));

        // Act and assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => tasks.ReadPageAsync(
                PersonalTaskOrigin.Asserted,
                null,
                null,
                TestContext.Current.CancellationToken));
    }

    /// <summary>A grant that writes no mailbox still writes this list, which is the whole reason every act here is admitted under the reading grant.</summary>
    [Fact]
    public async Task RecordAsync_ACallerGrantedOnlyReading_WritesTheTask()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();

        // Act
        await SignedIn(store).RecordAsync("keep a list", dueOn: null, sourceMessage: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("keep a list", Assert.Single(store.Held).Title);
    }

    private static OwnTasks SignedIn(IPersonalTaskStore store) => Signed(store, User);

    private static OwnTasks Signed(IPersonalTaskStore store, MailUserId user) => new(
        AccessAuthorizations.ForUserGranted(user, MailFathomPermission.MailRead),
        store,
        new FakeTimeProvider(Stamped));

    private static async Task<PersonalTask> KeepAsync(
        InMemoryPersonalTaskStore store,
        MailUserId user,
        string title,
        DateOnly? dueOn,
        PersonalTaskOrigin origin = PersonalTaskOrigin.Asserted)
    {
        var task = PersonalTask.Compose(
            PersonalTaskId.Create(Guid.CreateVersion7(Stamped + TimeSpan.FromMinutes(store.Held.Count))),
            user,
            title,
            dueOn,
            origin,
            sourceMessage: null);

        await store.AddAsync(task, TestContext.Current.CancellationToken);

        return task;
    }
}

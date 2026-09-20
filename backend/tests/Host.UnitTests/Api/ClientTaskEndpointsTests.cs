// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Tasks;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>
/// Covers the eight routes a person reads and writes their own task list over. What is decided here rather than in the
/// use case beneath is the transport half: what a request has to state before it is a task at all, which refusals name
/// a rule rather than echo a value, and that a task this person does not hold is answered as one that does not exist
/// whichever route named it.
/// </summary>
public sealed class ClientTaskEndpointsTests
{
    private static readonly MailUserId User = SyntheticMailUser.Deployment;

    private static readonly DateTimeOffset Stamped = new(2026, 9, 20, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid TaskIdentifier = new("2f0b1c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");

    private static readonly Guid MessageIdentifier = new("0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90");

    [Fact]
    public async Task ReadCommittedAsync_APageOfTasks_DescribesEachRowAndTheBoundaryTheNextPageContinuesFrom()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.ReadAsync(
                User,
                PersonalTaskOrigin.Asserted,
                null,
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => [Kept(dueOn: new DateOnly(2026, 9, 21))]);

        // Act
        var result = await ClientTaskEndpoints.ReadCommittedAsync(
            pageSize: 1,
            cursor: null,
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        var page = Assert.IsType<Ok<ClientTaskPageResponse>>(result.Result).Value!;
        var row = Assert.Single(page.Tasks);

        Assert.Equal(TaskIdentifier, row.Id);
        Assert.Equal("Answer the tender", row.Title);
        Assert.Equal("2026-09-21", row.DueOn);
        Assert.Equal("Asserted", row.Origin);
        Assert.False(row.Completed);
        Assert.Equal(MessageIdentifier, row.SourceMessageId);
        Assert.NotNull(page.NextCursor);
    }

    /// <summary>The proposals are the other half of the list, and the route is what says which half a page came from.</summary>
    [Fact]
    public async Task ReadProposedAsync_APageOfProposals_ReadsTheProposedOriginRatherThanTheWholeList()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.ReadAsync(
                User,
                PersonalTaskOrigin.Proposed,
                null,
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => [Kept(dueOn: null, origin: PersonalTaskOrigin.Proposed)]);

        // Act
        var result = await ClientTaskEndpoints.ReadProposedAsync(
            pageSize: null,
            cursor: null,
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        var page = Assert.IsType<Ok<ClientTaskPageResponse>>(result.Result).Value!;

        Assert.Equal("Proposed", Assert.Single(page.Tasks).Origin);
        Assert.Null(Assert.Single(page.Tasks).DueOn);
    }

    /// <summary>A boundary this deployment never issued names no page, and the first one would be a list silently jumping back to the top.</summary>
    [Fact]
    public async Task ReadCommittedAsync_ACursorThisDeploymentDidNotIssue_RefusesWithoutEchoingIt()
    {
        // Act
        var result = await ClientTaskEndpoints.ReadCommittedAsync(
            pageSize: null,
            cursor: "not-a-cursor-this-deployment-issued",
            SignedIn(Substitute.For<IPersonalTaskStore>()),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.DoesNotContain("not-a-cursor", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>A query string is composed by a page rather than typed, so a screen with nothing to continue from sends an empty value rather than none.</summary>
    [Fact]
    public async Task ReadCommittedAsync_AnEmptyCursor_IsReadAsTheFirstPageRatherThanRefused()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.ReadAsync(User, PersonalTaskOrigin.Asserted, null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => []);

        // Act
        var result = await ClientTaskEndpoints.ReadCommittedAsync(
            pageSize: null,
            cursor: "  ",
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<Ok<ClientTaskPageResponse>>(result.Result);
    }

    [Fact]
    public async Task FindAsync_ATaskOfTheirOwn_DescribesIt()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.FindAsync(User, PersonalTaskId.Create(TaskIdentifier), Arg.Any<CancellationToken>())
            .Returns(Kept(new DateOnly(2026, 9, 21)));

        // Act
        var result = await ClientTaskEndpoints.FindAsync(
            TaskIdentifier,
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TaskIdentifier, Assert.IsType<Ok<ClientTaskResponse>>(result.Result).Value!.Id);
    }

    /// <summary>The route constraint admits the all-zero identifier like any other, and no task carries it.</summary>
    [Fact]
    public async Task FindAsync_TheIdentifierNoTaskCarries_AnswersAsOneThatDoesNotExist()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();

        // Act
        var result = await ClientTaskEndpoints.FindAsync(
            Guid.Empty,
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
        await store.DidNotReceiveWithAnyArgs().FindAsync(default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RecordAsync_ATaskAPersonStated_WritesItAndDescribesWhatWasWritten()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();

        // Act
        var result = await ClientTaskEndpoints.RecordAsync(
            new ClientTaskRecordRequest("Answer the tender", "2026-09-21", MessageIdentifier),
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        var written = Assert.IsType<Ok<ClientTaskResponse>>(result.Result).Value!;

        Assert.Equal("Answer the tender", written.Title);
        Assert.Equal("2026-09-21", written.DueOn);
        Assert.Equal("Asserted", written.Origin);
        Assert.Equal(MessageIdentifier, written.SourceMessageId);

        await store.Received(1).AddAsync(
            Arg.Is<PersonalTask>(task => task != null && task.User == User && task.Title == "Answer the tender"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A task with no day is one nobody has dated, which is a state rather than a request to refuse.</summary>
    [Fact]
    public async Task RecordAsync_ATaskWithNoDay_WritesItUndated()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();

        // Act
        var result = await ClientTaskEndpoints.RecordAsync(
            new ClientTaskRecordRequest("Answer the tender", DueOn: null, SourceMessageId: null),
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        var written = Assert.IsType<Ok<ClientTaskResponse>>(result.Result).Value!;

        Assert.Null(written.DueOn);
        Assert.Null(written.SourceMessageId);
    }

    /// <summary>Every refusal names the rule rather than the value, because a problem document is the part of an answer a proxy log keeps.</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("Answer the tender", "21/09/2026")]
    [InlineData("Answer the tender", "2026-9-21")]
    public async Task RecordAsync_ARequestThatStatesNoTask_RefusesWithoutEchoingIt(string? title, string? dueOn)
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();

        // Act
        var result = await ClientTaskEndpoints.RecordAsync(
            new ClientTaskRecordRequest(title, dueOn, SourceMessageId: null),
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        await store.DidNotReceiveWithAnyArgs().AddAsync(default!, TestContext.Current.CancellationToken);
    }

    /// <summary>The bound is stated rather than thrown, because an overlong title is a client to inform rather than a producer to stop.</summary>
    [Fact]
    public async Task RecordAsync_ATitleLongerThanTheBound_RefusesNamingTheBound()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();

        // Act
        var result = await ClientTaskEndpoints.RecordAsync(
            new ClientTaskRecordRequest(
                new string('x', PersonalTask.MaximumTitleLength + 1),
                DueOn: null,
                SourceMessageId: null),
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Contains(
            PersonalTask.MaximumTitleLength.ToString(CultureInfo.InvariantCulture),
            refusal.ProblemDetails.Detail,
            StringComparison.Ordinal);
        await store.DidNotReceiveWithAnyArgs().AddAsync(default!, TestContext.Current.CancellationToken);
    }

    /// <summary>A request that carried no document at all is a request to refuse rather than a task with nothing in it.</summary>
    [Fact]
    public async Task RecordAsync_ARequestWithNoBody_Refuses()
    {
        // Act
        var result = await ClientTaskEndpoints.RecordAsync(
            request: null,
            SignedIn(Substitute.For<IPersonalTaskStore>()),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task ReviseAsync_ATaskOfTheirOwn_WritesTheLineAndTheDayAndDescribesTheResult()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.FindAsync(User, PersonalTaskId.Create(TaskIdentifier), Arg.Any<CancellationToken>())
            .Returns(Kept(new DateOnly(2026, 9, 21)));
        store.ReviseAsync(Arg.Any<PersonalTask>(), Arg.Any<CancellationToken>())
            .Returns(PersonalTaskChangeOutcome.Applied);

        // Act
        var result = await ClientTaskEndpoints.ReviseAsync(
            TaskIdentifier,
            new ClientTaskRecordRequest("Answer the tender today", "2026-09-22", SourceMessageId: null),
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        var revised = Assert.IsType<Ok<ClientTaskResponse>>(result.Result).Value!;

        Assert.Equal("Answer the tender today", revised.Title);
        Assert.Equal("2026-09-22", revised.DueOn);
        Assert.Equal(MessageIdentifier, revised.SourceMessageId);
    }

    /// <summary>A task this person does not hold is answered as one that does not exist, so nothing reports whose tasks there are.</summary>
    [Fact]
    public async Task ReviseAsync_ATaskThisPersonDoesNotHold_AnswersAsOneThatDoesNotExist()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.FindAsync(User, PersonalTaskId.Create(TaskIdentifier), Arg.Any<CancellationToken>())
            .Returns((PersonalTask?)null);

        // Act
        var result = await ClientTaskEndpoints.ReviseAsync(
            TaskIdentifier,
            new ClientTaskRecordRequest("Answer the tender today", DueOn: null, SourceMessageId: null),
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
        await store.DidNotReceiveWithAnyArgs().ReviseAsync(default!, TestContext.Current.CancellationToken);
    }

    /// <summary>The identifier is read before the body, so a route naming no task refuses as a missing task rather than as a malformed one.</summary>
    [Fact]
    public async Task ReviseAsync_TheIdentifierNoTaskCarries_AnswersAsOneThatDoesNotExist()
    {
        // Act
        var result = await ClientTaskEndpoints.ReviseAsync(
            Guid.Empty,
            new ClientTaskRecordRequest("Answer the tender today", DueOn: null, SourceMessageId: null),
            SignedIn(Substitute.For<IPersonalTaskStore>()),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    [Fact]
    public async Task SetCompletionAsync_ATaskOfTheirOwn_DescribesTheTaskAsItNowStands()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.SetCompletionAsync(User, PersonalTaskId.Create(TaskIdentifier), true, Arg.Any<CancellationToken>())
            .Returns(PersonalTaskChangeOutcome.Applied);
        store.FindAsync(User, PersonalTaskId.Create(TaskIdentifier), Arg.Any<CancellationToken>())
            .Returns(Kept(new DateOnly(2026, 9, 21), isCompleted: true));

        // Act
        var result = await ClientTaskEndpoints.SetCompletionAsync(
            TaskIdentifier,
            new ClientTaskCompletionRequest(Completed: true),
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Assert.IsType<Ok<ClientTaskResponse>>(result.Result).Value!.Completed);
    }

    [Fact]
    public async Task SetCompletionAsync_ATaskThisPersonDoesNotHold_AnswersAsOneThatDoesNotExist()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.SetCompletionAsync(User, PersonalTaskId.Create(TaskIdentifier), true, Arg.Any<CancellationToken>())
            .Returns(PersonalTaskChangeOutcome.NotFound);

        // Act
        var result = await ClientTaskEndpoints.SetCompletionAsync(
            TaskIdentifier,
            new ClientTaskCompletionRequest(Completed: true),
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    [Fact]
    public async Task AcceptAsync_AProposalOfTheirOwn_DescribesTheTaskAsItNowStands()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.AcceptAsync(User, PersonalTaskId.Create(TaskIdentifier), Arg.Any<CancellationToken>())
            .Returns(PersonalTaskChangeOutcome.Applied);
        store.FindAsync(User, PersonalTaskId.Create(TaskIdentifier), Arg.Any<CancellationToken>())
            .Returns(Kept(dueOn: null));

        // Act
        var result = await ClientTaskEndpoints.AcceptAsync(
            TaskIdentifier,
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Asserted", Assert.IsType<Ok<ClientTaskResponse>>(result.Result).Value!.Origin);
    }

    [Fact]
    public async Task AcceptAsync_ATaskThisPersonDoesNotHold_AnswersAsOneThatDoesNotExist()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.AcceptAsync(User, PersonalTaskId.Create(TaskIdentifier), Arg.Any<CancellationToken>())
            .Returns(PersonalTaskChangeOutcome.NotFound);

        // Act
        var result = await ClientTaskEndpoints.AcceptAsync(
            TaskIdentifier,
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    /// <summary>Dismissing a proposal is this same erasure, which is why the answer says what happened rather than what was asked for.</summary>
    [Fact]
    public async Task EraseAsync_ATaskOfTheirOwn_ReportsThatItWent()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.EraseAsync(User, PersonalTaskId.Create(TaskIdentifier), Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var result = await ClientTaskEndpoints.EraseAsync(
            TaskIdentifier,
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Value!.Erased);
        Assert.Equal(TaskIdentifier, result.Value.Id);
    }

    /// <summary>A task already gone and one another person holds are the same answer, and neither is an error.</summary>
    [Fact]
    public async Task EraseAsync_ATaskThisPersonDoesNotHold_ReportsThatNothingWent()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.EraseAsync(User, PersonalTaskId.Create(TaskIdentifier), Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var result = await ClientTaskEndpoints.EraseAsync(
            TaskIdentifier,
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Value!.Erased);
    }

    /// <summary>The identifier no task carries reaches no store, because an erasure of nothing is what a caller that named nothing asked for.</summary>
    [Fact]
    public async Task EraseAsync_TheIdentifierNoTaskCarries_ReportsThatNothingWent()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();

        // Act
        var result = await ClientTaskEndpoints.EraseAsync(
            Guid.Empty,
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Value!.Erased);
        await store.DidNotReceiveWithAnyArgs().EraseAsync(default, default, TestContext.Current.CancellationToken);
    }

    private static OwnTasks SignedIn(IPersonalTaskStore store) => new(
        AccessAuthorizations.ForUserGranted(User, MailFathomPermission.MailRead),
        store,
        new FakeTimeProvider(Stamped));

    private static PersonalTask Kept(
        DateOnly? dueOn,
        PersonalTaskOrigin origin = PersonalTaskOrigin.Asserted,
        bool isCompleted = false) =>
        PersonalTask.Restore(
            PersonalTaskId.Create(TaskIdentifier),
            User,
            "Answer the tender",
            dueOn,
            origin,
            StoredEmailId.Create(MessageIdentifier),
            isCompleted);
}

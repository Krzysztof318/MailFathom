// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Streaming;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the three Discover routes accept off the wire, what they refuse, and what a reader is handed back.</summary>
/// <remarks>
/// <para>
/// The run itself is covered where it happens. What is asserted here is the transport: which questions are refused
/// before a run is opened at all, that a run belongs to the user who asked for it whether it is being read or stopped,
/// and that a client holding a cursor is given the tail rather than the run over again.
/// </para>
/// <para>
/// <strong>The replica that did not start the run is the shape most of these are written in.</strong> Reading and
/// stopping are given the store alone, with nothing registered as executing here, which is exactly what a request
/// routed to any other replica meets — so a route that only worked where the run was composed fails these rather than
/// passing until somebody raises the replica count.
/// </para>
/// </remarks>
public sealed class ClientDiscoveryRunEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void DiscoveryRunsRoute_IsThePathAClientComposes() =>
        Assert.Equal("/discovery/runs", ClientDiscoveryRunEndpoints.DiscoveryRunsRoute);

    /// <summary>Reading and stopping are one address, which a client composes from a constant of its own.</summary>
    [Fact]
    public void DiscoveryRunRoute_IsThePathAClientComposes() =>
        Assert.Equal("/discovery/runs/{runId:guid}", ClientDiscoveryRunEndpoints.DiscoveryRunRoute);

    /// <summary>A question the user may ask opens a run and answers with where that run is read, which is all a client needs.</summary>
    [Fact]
    public async Task Start_AQuestionOverTheUsersOwnMail_OpensARunAndNamesWhereItIsRead()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();

        // Act
        var answered = await StartAsync(
            new ClientDiscoveryRunRequest("which supplier quoted least", null, null, null, null),
            store);

        // Assert
        var accepted = Assert.IsType<Accepted<ClientDiscoveryRunResponse>>(answered.Result);
        Assert.NotNull(accepted.Value);
        Assert.Equal(1, store.HeldCount);
        Assert.Equal($"/api/client/discovery/runs/{accepted.Value.RunId}", accepted.Location);
    }

    /// <summary>A question with no text is refused where a person can act on it rather than opening a run that fails at once.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Start_AQuestionCarryingNoText_RefusesItBeforeOpeningARun(string? question)
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();

        // Act
        var answered = await StartAsync(new ClientDiscoveryRunRequest(question, null, null, null, null), store);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(answered.Result).StatusCode);
        Assert.Equal(0, store.HeldCount);
    }

    /// <summary>A role no account maps a folder with is a question to correct, not a fault in the deployment.</summary>
    /// <remarks>
    /// A folder may be named by role, which is what a screen offering "the junk folder" sends, and a deployment mapping
    /// no such folder resolves it to nothing. That is the caller's request to change — so it is refused here by name,
    /// rather than left to escape into the generic handler and come back as an opaque fault this endpoint's own contract
    /// promises it will not answer with.
    /// </remarks>
    [Fact]
    public async Task Start_ARoleNoAccountMapsAFolderWith_RefusesTheQuestion()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();

        // Act
        var answered = await StartAsync(
            new ClientDiscoveryRunRequest("which supplier quoted least", null, ["role:Junk"], null, null),
            store);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(answered.Result).StatusCode);
        Assert.Equal(0, store.HeldCount);
    }

    /// <summary>An account this user is not assigned is refused as a request to change rather than narrowed away in silence.</summary>
    [Fact]
    public async Task Start_AnAccountThisUserDoesNotOwn_RefusesTheQuestion()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();

        // Act
        var answered = await StartAsync(
            new ClientDiscoveryRunRequest("which supplier quoted least", ["somebody-elses"], null, null, null),
            store);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(answered.Result).StatusCode);
        Assert.Equal(0, store.HeldCount);
    }

    /// <summary>A run executes as whoever asked for it, so a request the transport admitted nobody for opens none.</summary>
    [Fact]
    public async Task Start_ARequestNoPrincipalWasAdmittedFor_RefusesTheQuestion()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var principals = Substitute.For<IAuthorizedPrincipalSource>();
        principals.Current.Returns((AuthorizedPrincipal?)null);

        // Act
        var answered = await ClientDiscoveryRunEndpoints.Start(
            new ClientDiscoveryRunRequest("which supplier quoted least", null, null, null, null),
            ResolverFor(SyntheticMailUser.Deployment),
            MailUserClocks.Reading(Now),
            principals,
            store,
            new FakeTimeProvider(Now),
            Launcher(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(answered.Result).StatusCode);
        Assert.Equal(0, store.HeldCount);
    }

    /// <summary>A client looping over the asking route is told to wait, and the number it is told is this person's across the deployment.</summary>
    [Fact]
    public async Task Start_BeyondWhatOnePersonRunsAtOnce_RefusesWithTooManyRequests()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();

        for (var run = 0; run < DiscoveryRunBounds.MaximumConcurrentRunsPerUser; run++)
        {
            await store.TryOpenAsync(
                DiscoveryRunId.New(),
                SyntheticMailUser.Deployment,
                Now,
                TestContext.Current.CancellationToken);
        }

        // Act
        var answered = await StartAsync(
            new ClientDiscoveryRunRequest("which supplier quoted least", null, null, null, null),
            store);

        // Assert
        var refused = Assert.IsType<ProblemHttpResult>(answered.Result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, refused.StatusCode);
        Assert.Contains(
            $"{DiscoveryRunBounds.MaximumConcurrentRunsPerUser} questions at once",
            refused.ProblemDetails.Detail,
            StringComparison.Ordinal);
    }

    /// <summary>An identifier alone says nothing about whether a run exists, so somebody else's reads as no such run.</summary>
    [Fact]
    public async Task Read_ARunAnotherUserStarted_ReportsNoSuchRun()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var id = await WrittenRunAsync(store, SyntheticMailUser.Another);

        // Act
        var answered = await ReadAsync(id.Value, store);

        // Assert
        Assert.IsType<NotFound>(answered.Result);
    }

    /// <summary>An identifier no run could carry is answered as no such run rather than reaching the store at all.</summary>
    [Fact]
    public async Task Read_AnIdentifierNoRunCouldCarry_ReportsNoSuchRun() =>
        Assert.IsType<NotFound>((await ReadAsync(Guid.Empty, new InMemoryDiscoveryRunStore())).Result);

    /// <summary>A run is read whole by a replica that never executed it, which is the whole of what a durable run buys.</summary>
    [Fact]
    public async Task Read_ByAReplicaThatDidNotStartTheRun_ReturnsEveryEventAndSaysTheRunIsOver()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var id = await WrittenRunAsync(store, SyntheticMailUser.Deployment);

        // Act
        var answered = await ReadAsync(id.Value, store);

        // Assert
        var read = Assert.IsType<Ok<ClientDiscoveryRunReadResponse>>(answered.Result);
        Assert.NotNull(read.Value);
        Assert.Equal([1L, 2L], read.Value.Events.Select(@event => @event.Sequence));
        Assert.False(read.Value.Running);
    }

    /// <summary>A client holding a cursor is given the tail, which is what makes a burst of advances cost one read.</summary>
    [Fact]
    public async Task Read_FromACursor_ReturnsOnlyWhatCameAfterIt()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var id = await WrittenRunAsync(store, SyntheticMailUser.Deployment);

        // Act
        var answered = await ReadAsync(id.Value, store, since: 1);

        // Assert
        var read = Assert.IsType<Ok<ClientDiscoveryRunReadResponse>>(answered.Result);
        Assert.NotNull(read.Value);
        Assert.Equal([2L], read.Value.Events.Select(@event => @event.Sequence));
    }

    /// <summary>A run still executing says so, which is what tells a client watching it to expect more.</summary>
    [Fact]
    public async Task Read_ARunStillExecuting_SaysItIsStillRunning()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var id = DiscoveryRunId.New();
        await store.TryOpenAsync(id, SyntheticMailUser.Deployment, Now, TestContext.Current.CancellationToken);
        await store.AppendAsync(id, new DiscoveryRunStarted(), Now, TestContext.Current.CancellationToken);

        // Act
        var answered = await ReadAsync(id.Value, store);

        // Assert
        var read = Assert.IsType<Ok<ClientDiscoveryRunReadResponse>>(answered.Result);
        Assert.NotNull(read.Value);
        Assert.True(read.Value.Running);
    }

    /// <summary>A cursor naming a place this run never reached belongs to some other run, so the run is read whole.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9)]
    public async Task Read_ACursorNamingNoPlaceInThisRun_ReadsTheRunFromItsBeginning(int? since)
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var id = await WrittenRunAsync(store, SyntheticMailUser.Deployment);

        // Act
        var answered = await ReadAsync(id.Value, store, since);

        // Assert
        var read = Assert.IsType<Ok<ClientDiscoveryRunReadResponse>>(answered.Result);
        Assert.NotNull(read.Value);
        Assert.Equal([1L, 2L], read.Value.Events.Select(@event => @event.Sequence));
    }

    /// <summary>An event names its kind once and carries nothing of how this process reads the run it belongs to.</summary>
    /// <remarks>
    /// The name and the ending are read in process — one names a row's own kind column, the other stops the journal —
    /// and neither is part of what a client is told, which the discriminator already carries. The serializer reads the
    /// attribute that keeps them out off the member being written rather than off the one it overrides, so this is what
    /// fails when an event is added without repeating it.
    /// </remarks>
    [Fact]
    public async Task Read_ARunThisUserHolds_WritesTheKindOnceAndNothingThisProcessReadsTheRunBy()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var id = await WrittenRunAsync(store, SyntheticMailUser.Deployment);

        // Act
        var body = await ReadBodyAsync(id.Value, store);

        // Assert
        Assert.Contains($"\"event\":\"{DiscoveryRunStarted.Kind}\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("eventName", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("endsTheRun", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Why a run ended crosses the wire as the word a client branches on, which is the enum member's own name.</summary>
    /// <remarks>
    /// The value is a closed set a screen decides what to offer next from, so its spelling is the contract rather than an
    /// artefact of how it is serialized — nothing on this surface applies a naming policy to an enum, and a client
    /// matching a differently-cased word would fall through every branch it has and offer nothing.
    /// </remarks>
    [Fact]
    public async Task Read_ARunThatEndedBadly_WritesWhyAsTheClosedValuesOwnName()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var id = DiscoveryRunId.New();
        await store.TryOpenAsync(id, SyntheticMailUser.Deployment, Now, TestContext.Current.CancellationToken);
        await store.AppendAsync(id, new DiscoveryRunStarted(), Now, TestContext.Current.CancellationToken);
        await store.AppendAsync(
            id,
            new DiscoveryRunFailed(DiscoveryRunFailure.TimedOut, MailAnsweringRunSpend.Nothing),
            Now,
            TestContext.Current.CancellationToken);

        // Act
        var body = await ReadBodyAsync(id.Value, store);

        // Assert
        Assert.Contains("\"failure\":\"TimedOut\"", body, StringComparison.Ordinal);
    }

    /// <summary>What the run read of each account reaches the client on the ending, under the names the contract gives it.</summary>
    /// <remarks>
    /// Coverage is the part of the plan that never arrives as a block, so a client that ignored the ending would draw an
    /// answer without saying how current the mail behind it was. The names are asserted against the response rather than
    /// read off the type, because the serializer's own decisions are visible nowhere else.
    /// </remarks>
    [Fact]
    public async Task Read_ARunThatCompleted_WritesWhatItReadOfEachAccountOnTheEnding()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var id = DiscoveryRunId.New();
        await store.TryOpenAsync(id, SyntheticMailUser.Deployment, Now, TestContext.Current.CancellationToken);
        await store.AppendAsync(id, new DiscoveryRunStarted(), Now, TestContext.Current.CancellationToken);
        await store.AppendAsync(
            id,
            new DiscoveryRunCompleted(
                [],
                [
                    new AccountCoverage(
                        PresentationText.Create("work"),
                        PresentationFreshness.CurrentAt(Now),
                        Now.AddDays(-30),
                        Now),
                ],
                MailAnsweringRunSpend.Nothing),
            Now,
            TestContext.Current.CancellationToken);

        // Act
        var body = await ReadBodyAsync(id.Value, store);

        // Assert
        Assert.Contains("\"coverage\":[{", body, StringComparison.Ordinal);
        Assert.Contains("\"account\":\"work\"", body, StringComparison.Ordinal);
        Assert.Contains("\"staleness\":\"Current\"", body, StringComparison.Ordinal);
        Assert.Contains("\"earliestReceivedAt\":", body, StringComparison.Ordinal);
        Assert.Contains("\"latestReceivedAt\":", body, StringComparison.Ordinal);
    }

    /// <summary>A stop landing on the replica executing the run reaches the work as well as the record.</summary>
    [Fact]
    public async Task Stop_OnTheReplicaExecutingTheRun_RecordsItAndCancelsWhatTheRunIsWaitingOn()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var executing = new ExecutingDiscoveryRuns();
        var id = DiscoveryRunId.New();
        await store.TryOpenAsync(id, SyntheticMailUser.Deployment, Now, TestContext.Current.CancellationToken);

        using var journal = new DiscoveryRunJournal(
            id,
            SyntheticMailUser.Deployment,
            store,
            ClientSignalPublishers.ReachingNobody,
            new FakeTimeProvider(Now));
        executing.Register(journal);

        // Act
        var answered = await StopAsync(id.Value, store, executing);

        // Assert
        Assert.IsType<NoContent>(answered.Result);
        Assert.True(store.WasAskedToStop(id));
        Assert.True(journal.Stopping.IsCancellationRequested);
    }

    /// <summary>A stop landing on a replica that is executing nothing is still answered, because the record is what reaches the run.</summary>
    [Fact]
    public async Task Stop_OnAReplicaThatDidNotStartTheRun_RecordsItAndAnswersWithNoContent()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var id = DiscoveryRunId.New();
        await store.TryOpenAsync(id, SyntheticMailUser.Deployment, Now, TestContext.Current.CancellationToken);

        // Act
        var answered = await StopAsync(id.Value, store, new ExecutingDiscoveryRuns());

        // Assert
        Assert.IsType<NoContent>(answered.Result);
        Assert.True(store.WasAskedToStop(id));
    }

    /// <summary>A run that finished a moment earlier is stopped successfully, because whoever asked could not have known.</summary>
    [Fact]
    public async Task Stop_ARunThatHasAlreadyEnded_AnswersWithNoContentAndRecordsNoStop()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var id = await WrittenRunAsync(store, SyntheticMailUser.Deployment);

        // Act
        var answered = await StopAsync(id.Value, store, new ExecutingDiscoveryRuns());

        // Assert
        Assert.IsType<NoContent>(answered.Result);
        Assert.False(store.WasAskedToStop(id));
    }

    /// <summary>An identifier is a bearer value, so stopping somebody else's run reads as no such run and leaves it running.</summary>
    [Fact]
    public async Task Stop_ARunAnotherUserStarted_ReportsNoSuchRunAndLeavesItRunning()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var id = DiscoveryRunId.New();
        await store.TryOpenAsync(id, SyntheticMailUser.Another, Now, TestContext.Current.CancellationToken);

        // Act
        var answered = await StopAsync(id.Value, store, new ExecutingDiscoveryRuns());

        // Assert
        Assert.IsType<NotFound>(answered.Result);
        Assert.False(store.WasAskedToStop(id));
    }

    /// <summary>An identifier no run could carry is answered as no such run rather than reaching the store at all.</summary>
    [Fact]
    public async Task Stop_AnIdentifierNoRunCouldCarry_ReportsNoSuchRun() =>
        Assert.IsType<NotFound>(
            (await StopAsync(Guid.Empty, new InMemoryDiscoveryRunStore(), new ExecutingDiscoveryRuns())).Result);

    private static Task<Results<NoContent, NotFound>> StopAsync(
        Guid runId,
        InMemoryDiscoveryRunStore store,
        ExecutingDiscoveryRuns executing) =>
        ClientDiscoveryRunEndpoints.Stop(
            runId,
            ResolverFor(SyntheticMailUser.Deployment),
            store,
            executing,
            new FakeTimeProvider(Now),
            TestContext.Current.CancellationToken);

    private static Task<Results<Accepted<ClientDiscoveryRunResponse>, ProblemHttpResult>> StartAsync(
        ClientDiscoveryRunRequest request,
        InMemoryDiscoveryRunStore store) =>
        ClientDiscoveryRunEndpoints.Start(
            request,
            ResolverFor(SyntheticMailUser.Deployment),
            MailUserClocks.Reading(Now),
            AdmittedCaller(SyntheticMailUser.Deployment),
            store,
            new FakeTimeProvider(Now),
            Launcher(),
            TestContext.Current.CancellationToken);

    private static Task<Results<Ok<ClientDiscoveryRunReadResponse>, NotFound>> ReadAsync(
        Guid runId,
        InMemoryDiscoveryRunStore store,
        long? since = null) =>
        ClientDiscoveryRunEndpoints.Read(
            runId,
            since,
            ResolverFor(SyntheticMailUser.Deployment),
            store,
            new FakeTimeProvider(Now),
            TestContext.Current.CancellationToken);

    /// <summary>Runs the read's result against a request and reads what went out.</summary>
    /// <remarks>
    /// The result is executed rather than inspected, because what a client reads is the serializer's own decisions about
    /// the events inside it, and those exist only once the result has written them.
    /// </remarks>
    private static async Task<string> ReadBodyAsync(Guid runId, InMemoryDiscoveryRunStore store)
    {
        var answered = await ReadAsync(runId, store);
        var body = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        context.Response.Body = body;

        await Assert.IsType<Ok<ClientDiscoveryRunReadResponse>>(answered.Result).ExecuteAsync(context);

        return Encoding.UTF8.GetString(body.ToArray());
    }

    private static async Task<DiscoveryRunId> WrittenRunAsync(InMemoryDiscoveryRunStore store, MailUserId user)
    {
        var id = DiscoveryRunId.New();
        await store.TryOpenAsync(id, user, Now, TestContext.Current.CancellationToken);
        await store.AppendAsync(id, new DiscoveryRunStarted(), Now, TestContext.Current.CancellationToken);
        await store.AppendAsync(
            id,
            new DiscoveryRunCompleted([], [], MailAnsweringRunSpend.Nothing),
            Now,
            TestContext.Current.CancellationToken);

        return id;
    }

    private static IAuthorizedPrincipalSource AdmittedCaller(MailUserId user)
    {
        var principals = Substitute.For<IAuthorizedPrincipalSource>();
        principals.Current.Returns(
            AuthorizedPrincipal.CallerActingFor(user, "test-caller", [MailFathomPermission.MailAsk]));

        return principals;
    }

    private static MailboxScopeResolver ResolverFor(MailUserId user) =>
        new(
            AssignedMailAccountCatalogs.For(
                AccessAuthorizations.ForUserGranted(user, MailFathomPermission.MailAsk),
                SyntheticServedAccount.Of("primary")),
            StubMailFolderParticipation.Nothing,
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.Nothing.Resolver);

    /// <summary>Builds the launcher the asking route hands a run to, over a store of its own.</summary>
    /// <remarks>
    /// What these tests assert is what the route answered with, and the execution it starts is nobody's subject here —
    /// it is covered where it happens. So the launcher is given a store nothing opened the run in, which refuses every
    /// write it attempts: the run this class is asserting about is left exactly as the route wrote it, rather than
    /// being written to from a background task while an assertion reads it.
    /// </remarks>
    private static DiscoveryRunLauncher Launcher() =>
        new(
            Substitute.For<IServiceScopeFactory>(),
            new InMemoryDiscoveryRunStore(),
            ClientSignalPublishers.ReachingNobody,
            new ExecutingDiscoveryRuns(),
            Substitute.For<IHostApplicationLifetime>(),
            new FakeTimeProvider(Now),
            NullLogger<DiscoveryRunLauncher>.Instance);
}

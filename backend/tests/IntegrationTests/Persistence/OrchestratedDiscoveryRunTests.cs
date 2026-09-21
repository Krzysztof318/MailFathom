// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Discovery.Streaming;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves that a Discover run written on one replica is read, stopped, and forgotten from another.</summary>
/// <remarks>
/// <para>
/// This is the property the store exists for and the whole of what a durable run buys. Every one of its five statements
/// decides something in one composed command — the sequence derived inside the insert, the concurrency bound counted
/// inside it, the stop that refuses the next ordinary write but never an ending, the cursor read that falls back to the
/// beginning, and the two retention windows — so a fake reproducing them in a dictionary is a different mechanism in a
/// different process, which is exactly the per-replica arrangement this change replaced.
/// </para>
/// <para>
/// Two hosts rather than two calls on one, because a run composed on one replica and read on another is the case that
/// used to be unreachable: the events were held in memory beside the connection that streamed them. Each host composes
/// its own service graph over the one orchestrated database, which is what a second replica is.
/// </para>
/// <para>
/// A user of this class's own, provisioned and erased around each case, because both tables cascade from the user
/// record and the suite shares one database.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedDiscoveryRunTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>How many replicas write to one run at once, enough that an execution reliably loses a race for the next sequence.</summary>
    private const int ContendingReplicas = 8;

    /// <summary>What the composed block says, which is invented here rather than drawn from anything a mailbox holds.</summary>
    private const string AnswerText = "Nothing in the mailbox answers the question.";

    /// <summary>The instant every statement here is stamped with, so a retention window is this class's rather than the wall clock's.</summary>
    private static readonly DateTimeOffset Instant = new(2026, 9, 20, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A run one replica composed is read whole by another, in the order it was written and with what it wrote.</summary>
    /// <remarks>
    /// <para>
    /// The payload crosses a column through a polymorphic serializer, so this is also where a derived event that lost
    /// its discriminator would be found: a run read back as a list of base events renders as nothing on a screen and
    /// fails no unit test.
    /// </para>
    /// <para>
    /// <strong>A composed block is in the run for that reason and not as a fourth event.</strong> It is the one kind
    /// whose own member sorts before the discriminator, so a column that reorders what it stores loses this run and no
    /// other — which is what a round trip in one process cannot see, both halves of it being the writer's own order.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ReadAsync_ARunAnotherReplicaComposed_ReadsEveryEventInTheOrderItWasWritten()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var executingHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var readingHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(executingHost, user, cancellationToken);

        try
        {
            // Arrange
            var executing = await StoreOfAsync(executingHost, cancellationToken);
            var reading = await StoreOfAsync(readingHost, cancellationToken);
            var run = await OpenedAsync(executing, user, cancellationToken);

            await executing.AppendAsync(run, new DiscoveryRunStarted(), Instant, cancellationToken);
            await executing.AppendAsync(run, Progressed(1), Instant, cancellationToken);
            await executing.AppendAsync(run, Composed(), Instant, cancellationToken);
            await executing.AppendAsync(run, Completed(), Instant, cancellationToken);

            // Act
            var read = await reading.ReadAsync(run, MailUserId.Create(user), afterSequence: 0, Instant, cancellationToken);

            // Assert
            Assert.NotNull(read);
            Assert.False(read.Running);
            Assert.Equal([1L, 2L, 3L, 4L], read.Events.Select(written => written.Sequence));
            Assert.Equal(
                [
                    DiscoveryRunStarted.Kind,
                    DiscoveryRetrievalProgressed.Kind,
                    DiscoveryBlockComposed.Kind,
                    DiscoveryRunCompleted.Kind,
                ],
                read.Events.Select(written => written.EventName));
            Assert.All(read.Events, written => Assert.Equal(run, written.RunId));
            Assert.Equal(
                AnswerText,
                Assert.IsType<AnswerBlock>(Assert.IsType<DiscoveryBlockComposed>(read.Events[2]).Block).Text.Value);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(executingHost, user);
        }
    }

    /// <summary>A client's cursor hands back the tail alone, and one naming a place this run never reached reads it whole.</summary>
    /// <remarks>
    /// Both halves are one <c>CASE</c> inside the read's own statement rather than a decision either replica takes, which
    /// is what makes a cursor left over from an earlier run read as the beginning rather than as an empty answer a
    /// client would wait on forever.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_FromACursor_ReturnsTheTailAndReadsAnUnreachedCursorFromTheBeginning()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(host, user, cancellationToken);

        try
        {
            // Arrange
            var store = await StoreOfAsync(host, cancellationToken);
            var run = await OpenedAsync(store, user, cancellationToken);

            await store.AppendAsync(run, new DiscoveryRunStarted(), Instant, cancellationToken);
            await store.AppendAsync(run, Progressed(1), Instant, cancellationToken);
            await store.AppendAsync(run, Completed(), Instant, cancellationToken);

            // Act
            var tail = await store.ReadAsync(run, MailUserId.Create(user), afterSequence: 2, Instant, cancellationToken);
            var unreached = await store.ReadAsync(run, MailUserId.Create(user), afterSequence: 99, Instant, cancellationToken);
            var somebodyElses = await store.ReadAsync(run, MailUserId.Create(Guid.NewGuid()), afterSequence: 0, Instant, cancellationToken);

            // Assert
            Assert.Equal([3L], tail?.Events.Select(written => written.Sequence));
            Assert.Equal([1L, 2L, 3L], unreached?.Events.Select(written => written.Sequence));
            Assert.Null(somebodyElses);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(host, user);
        }
    }

    /// <summary>A stop recorded on one replica reaches the execution on another as the first write the store refuses, and still admits the ending.</summary>
    /// <remarks>
    /// This is how a person stopping their run reaches the replica spending against a provider, and it is the whole of
    /// that mechanism: there is no message and no shared token, only the condition on the append. The ending is exempt
    /// because the execution's own ledger is what says what the run spent, and an ending written by the stopping
    /// replica would be the one place a cost figure lies.
    /// </remarks>
    [Fact]
    public async Task AppendAsync_AfterAnotherReplicaRecordedAStop_RefusesTheNextWriteAndAdmitsTheEnding()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var executingHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var stoppingHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(executingHost, user, cancellationToken);

        try
        {
            // Arrange
            var executing = await StoreOfAsync(executingHost, cancellationToken);
            var stopping = await StoreOfAsync(stoppingHost, cancellationToken);
            var run = await OpenedAsync(executing, user, cancellationToken);
            await executing.AppendAsync(run, new DiscoveryRunStarted(), Instant, cancellationToken);

            // Act
            var recorded = await stopping.TryRequestStopAsync(run, MailUserId.Create(user), Instant, cancellationToken);
            var refused = await executing.AppendAsync(run, Progressed(1), Instant, cancellationToken);
            var ended = await executing.AppendAsync(
                run,
                new DiscoveryRunFailed(DiscoveryRunFailure.Cancelled, MailAnsweringRunSpend.Nothing),
                Instant,
                cancellationToken);

            // Assert
            Assert.True(recorded);
            Assert.Null(refused);
            Assert.Equal(2L, ended);
            Assert.False(await stopping.TryRequestStopAsync(run, MailUserId.Create(Guid.NewGuid()), Instant, cancellationToken));
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(executingHost, user);
        }
    }

    /// <summary>The sequence is derived inside the insert, so replicas writing at once leave a run numbered without a gap or a repeat.</summary>
    /// <remarks>
    /// Nothing below a real server settles this: what makes the number right is the composite primary key refusing a
    /// duplicate while the subquery reads the greatest sequence in the same statement. An implementation that read the
    /// maximum and then inserted would pass every sequential arrangement and would renumber a run under load, which a
    /// client reading by cursor would meet as an answer that silently loses a block.
    /// </remarks>
    [Fact]
    public async Task AppendAsync_FromSeveralWritersAtOnce_NumbersTheRunWithoutAGapOrARepeat()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var oneHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var anotherHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(oneHost, user, cancellationToken);

        try
        {
            // Arrange
            var oneReplica = await StoreOfAsync(oneHost, cancellationToken);
            var anotherReplica = await StoreOfAsync(anotherHost, cancellationToken);
            var run = await OpenedAsync(oneReplica, user, cancellationToken);

            // Act
            var attempts = await ConcurrentIdempotency.RunAsync(
                "Writing to one Discover run from several replicas at once",
                ContendingReplicas,
                (ordinal, token) => (ordinal % 2 == 0 ? oneReplica : anotherReplica)
                    .AppendAsync(run, Progressed(ordinal), Instant, token),
                cancellationToken);

            // Assert
            var read = await oneReplica.ReadAsync(run, MailUserId.Create(user), afterSequence: 0, Instant, cancellationToken);
            var written = read?.Events.Select(@event => @event.Sequence).ToArray() ?? [];
            Assert.Equal(attempts.Results.OfType<long>().Order(), written);
            Assert.Equal(Enumerable.Range(1, written.Length).Select(sequence => (long)sequence), written);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(oneHost, user);
        }
    }

    /// <summary>The concurrency bound is one person's across the deployment, counted wherever the run is opened.</summary>
    /// <remarks>
    /// The number an operator is told is what a person may run at once on the whole deployment, so it is counted in the
    /// table rather than per replica: a bound each process kept would let a deployment of four replicas run four times
    /// what its own refusal says, which is a spend figure that does not match the page.
    /// </remarks>
    [Fact]
    public async Task TryOpenAsync_PastWhatOnePersonMayRunAtOnce_RefusesTheNextRunOnEveryReplica()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var oneHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var anotherHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(oneHost, user, cancellationToken);

        try
        {
            // Arrange
            var oneReplica = await StoreOfAsync(oneHost, cancellationToken);
            var anotherReplica = await StoreOfAsync(anotherHost, cancellationToken);
            var opened = new List<DiscoveryRunId>();

            for (var run = 0; run < DiscoveryRunBounds.MaximumConcurrentRunsPerUser; run++)
            {
                opened.Add(await OpenedAsync(oneReplica, user, cancellationToken));
            }

            // Act
            var admittedNinth = await anotherReplica.TryOpenAsync(DiscoveryRunId.New(), MailUserId.Create(user), Instant, cancellationToken);
            await oneReplica.AppendAsync(opened[0], Completed(), Instant, cancellationToken);
            var admittedAfterOneEnded = await anotherReplica.TryOpenAsync(DiscoveryRunId.New(), MailUserId.Create(user), Instant, cancellationToken);

            // Assert
            Assert.False(admittedNinth);
            Assert.True(admittedAfterOneEnded);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(oneHost, user);
        }
    }

    /// <summary>The last place one person has left is given to one opening, however many reach for it together.</summary>
    /// <remarks>
    /// Two tabs, a retried click, and two replicas produce the same shape, and it is the shape the bound is lost in: a
    /// statement takes its snapshot before it runs, so openings arriving together would each count the runs the others
    /// had not committed yet and each find the room the last of them was going to take. Only a real database can show
    /// that, because what decides it is the isolation level and the lock the opening takes ahead of its count.
    /// </remarks>
    [Fact]
    public async Task TryOpenAsync_FromSeveralReplicasAtOnce_AdmitsOnlyWhatIsLeftOfThePersonsBound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var oneHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var anotherHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(oneHost, user, cancellationToken);

        try
        {
            // Arrange
            var oneReplica = await StoreOfAsync(oneHost, cancellationToken);
            var anotherReplica = await StoreOfAsync(anotherHost, cancellationToken);

            for (var run = 0; run < DiscoveryRunBounds.MaximumConcurrentRunsPerUser - 1; run++)
            {
                await OpenedAsync(oneReplica, user, cancellationToken);
            }

            // Act
            var attempts = await ConcurrentIdempotency.RunAsync(
                "Opening a Discover run from several replicas at once",
                ContendingReplicas,
                (ordinal, token) => (ordinal % 2 == 0 ? oneReplica : anotherReplica)
                    .TryOpenAsync(DiscoveryRunId.New(), MailUserId.Create(user), Instant, token),
                cancellationToken);

            // Assert
            Assert.Empty(attempts.Failures);
            Assert.Equal(1, attempts.Results.Count(admitted => admitted));
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(oneHost, user);
        }
    }

    /// <summary>A run past its retention window is removed with everything it composed, and erasing its user takes it too.</summary>
    /// <remarks>
    /// Both are storage-limitation obligations over rows composed from somebody's correspondence, and both are settled
    /// by the schema rather than by the sweep: the events cascade from the run and the run cascades from the user
    /// record, so a foreign key that lost its cascade would leave a person's answers behind after their record was
    /// erased and would fail no unit test.
    /// </remarks>
    [Fact]
    public async Task RemoveForgottenAsync_PastTheRetentionWindow_TakesTheRunAndEverythingItComposed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(host, user, cancellationToken);

        try
        {
            // Arrange
            var store = await StoreOfAsync(host, cancellationToken);
            var forgotten = await OpenedAsync(store, user, cancellationToken);
            var erased = await OpenedAsync(store, user, cancellationToken);

            await store.AppendAsync(forgotten, new DiscoveryRunStarted(), Instant, cancellationToken);
            await store.AppendAsync(forgotten, Completed(), Instant, cancellationToken);
            await store.AppendAsync(erased, new DiscoveryRunStarted(), Instant, cancellationToken);

            // Act
            await store.RemoveForgottenAsync(Instant + DiscoveryRunBounds.RetentionAfterLastUse, cancellationToken);

            // Assert
            Assert.Null(await store.ReadAsync(forgotten, MailUserId.Create(user), afterSequence: 0, Instant, cancellationToken));
            Assert.Equal(0, await CountEventsOfAsync(host, forgotten, cancellationToken));
            Assert.NotNull(await store.ReadAsync(erased, MailUserId.Create(user), afterSequence: 0, Instant, cancellationToken));

            await OrchestratedForeignUser.EraseAsync(host, user);

            Assert.Null(await store.ReadAsync(erased, MailUserId.Create(user), afterSequence: 0, Instant, cancellationToken));
            Assert.Equal(0, await CountEventsOfAsync(host, erased, cancellationToken));
        }
        finally
        {
            // Erased a second time on the ordinary path, which reports that there was nothing to erase rather than
            // failing. What the block is for is a case that ended before the erasure above.
            await OrchestratedForeignUser.EraseAsync(host, user);
        }
    }

    private static async Task<DiscoveryRunId> OpenedAsync(
        IDiscoveryRunStore store,
        Guid user,
        CancellationToken cancellationToken)
    {
        var id = DiscoveryRunId.New();

        Assert.True(await store.TryOpenAsync(id, MailUserId.Create(user), Instant, cancellationToken));

        return id;
    }

    private static DiscoveryRetrievalProgressed Progressed(int lookupsRun) =>
        new(
            new DiscoveryRetrievalProgress(lookupsRun, LookupsRefused: 0, LookupsPlanned: 6, PassagesFound: 0),
            MailAnsweringRunSpend.Nothing);

    private static DiscoveryBlockComposed Composed() =>
        new(new AnswerBlock(
            PresentationEvidence.Unsupported(PresentationFreshness.Unknown),
            PresentationText.Create(AnswerText),
            PresentationConfidence.Low));

    private static DiscoveryRunCompleted Completed() => new([], [], MailAnsweringRunSpend.Nothing);

    private static Task<int> CountEventsOfAsync(
        OrchestratedMailFathomServices host,
        DiscoveryRunId run,
        CancellationToken cancellationToken) => host.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .DiscoveryRunEvents
                .AsNoTracking()
                .Where(written => written.RunId == run.Value)
                .CountAsync(token),
            cancellationToken);

    private static Task<IDiscoveryRunStore> StoreOfAsync(
        OrchestratedMailFathomServices host,
        CancellationToken cancellationToken) => host.InScopeAsync(
            (scope, _) => Task.FromResult(scope.GetRequiredService<IDiscoveryRunStore>()),
            cancellationToken);
}

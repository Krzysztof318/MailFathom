// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Discovery.Streaming;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what becomes of a run this process could not execute at all, which nothing else would report.</summary>
/// <remarks>
/// The launcher's own contract is the handler around the run rather than the run itself. Nothing in a request awaits the
/// task it starts, so a fault escaping it would be observed by nobody and would leave a client re-reading a run that
/// never ends — and the use case deliberately writes only the endings it can name, because <c>Application</c> has no
/// logger to write the rest down in. What is asserted here is the other half of that arrangement: the fault is written
/// down, the run is ended in its journal, and the replica stops holding it, whichever of those the fault happened
/// before.
/// </remarks>
public sealed class DiscoveryRunLauncherTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);

    private static readonly MailQuestion Question = new(
        MailQuestionText.Create("which supplier quoted least"),
        MailboxScope.Create([], []),
        new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.FromHours(2)));

    /// <summary>A scope this process could not compose ends the run, rather than leaving a client re-reading one that never ends.</summary>
    [Fact]
    public async Task Start_AScopeThisProcessCannotCompose_EndsTheRunAsFailed()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var id = await OpenedAsync(store);

        // Act
        await LauncherOver(store, new ExecutingDiscoveryRuns(), NoLogger).Start(
            Question,
            id,
            SyntheticMailUser.Deployment,
            Caller);

        // Assert
        var written = store.Written(id);
        Assert.Equal(DiscoveryRunFailure.Failed, Assert.IsType<DiscoveryRunFailed>(written[^1]).Failure);
    }

    /// <summary>The failure the client is told nothing about is the one the operator reads, and it carries none of the question.</summary>
    /// <remarks>
    /// <see cref="DiscoveryRunFailure.Failed" /> promises a record exists somewhere an operator can reach, and this is
    /// the only place that promise is kept. It is also a record written about mail, so what it may carry is the fault
    /// and nothing the run was asked.
    /// </remarks>
    [Fact]
    public async Task Start_AScopeThisProcessCannotCompose_WritesTheFaultDownWithoutTheQuestion()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var id = await OpenedAsync(store);
        var logger = new RecordingLogger<DiscoveryRunLauncher>();

        // Act
        await LauncherOver(store, new ExecutingDiscoveryRuns(), logger).Start(
            Question,
            id,
            SyntheticMailUser.Deployment,
            Caller);

        // Assert
        var written = Assert.Single(logger.Messages);
        Assert.Contains("had no name for", written, StringComparison.Ordinal);
        Assert.DoesNotContain("supplier", written, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A run this replica is no longer executing is no longer held, whichever way its execution ended.</summary>
    /// <remarks>
    /// The run outlives the execution — it is read from the record afterwards, by any replica — so what is released here
    /// is only the token a stop landing on this replica would have reached it through. Holding it past the ending would
    /// grow without bound on a process that answers questions all day.
    /// </remarks>
    [Fact]
    public async Task Start_ARunWhoseExecutionEnded_StopsHoldingItOnThisReplica()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var id = await OpenedAsync(store);
        var executing = new ExecutingDiscoveryRuns();

        // Act
        await LauncherOver(store, executing, NoLogger).Start(
            Question,
            id,
            SyntheticMailUser.Deployment,
            Caller);

        // Assert
        Assert.Equal(0, executing.Count);
    }

    /// <summary>An execution that stopped without reaching an ending leaves the run ended, rather than pending until its ceiling.</summary>
    /// <remarks>
    /// This is the defensive half of the arrangement above, and the one a client cannot recover from on its own: a run
    /// left pending is re-read forever by whoever asked the question, and only the wider retention ceiling would
    /// eventually forget it. So the store is made to fail the ending the handler writes, which is the shape of the
    /// deployment coming apart mid-run, and what has to be left behind is still an ending.
    /// </remarks>
    [Fact]
    public async Task Start_AnExecutionThatStoppedWithoutEndingTheRun_EndsItAsStopped()
    {
        // Arrange
        var written = new List<DiscoveryRunEvent>();
        var store = Substitute.For<IDiscoveryRunStore>();
        store
            .AppendAsync(
                Arg.Any<DiscoveryRunId>(),
                Arg.Do<DiscoveryRunEvent>(written.Add),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new InvalidOperationException("the record could not be written"),
                _ => Task.FromResult<long?>(1));


        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => LauncherOver(store, new ExecutingDiscoveryRuns(), NoLogger).Start(
                Question,
                DiscoveryRunId.New(),
                SyntheticMailUser.Deployment,
                Caller));

        // Assert
        Assert.Equal(DiscoveryRunFailure.Stopped, Assert.IsType<DiscoveryRunFailed>(written[^1]).Failure);
    }

    private static AuthorizedPrincipal Caller =>
        AuthorizedPrincipal.CallerActingFor(
            SyntheticMailUser.Deployment,
            "test-caller",
            [MailFathomPermission.MailAsk]);

    private static ILogger<DiscoveryRunLauncher> NoLogger => new RecordingLogger<DiscoveryRunLauncher>();

    private static async Task<DiscoveryRunId> OpenedAsync(InMemoryDiscoveryRunStore store)
    {
        var id = DiscoveryRunId.New();
        await store.TryOpenAsync(id, SyntheticMailUser.Deployment, Now, TestContext.Current.CancellationToken);

        return id;
    }

    private static DiscoveryRunLauncher LauncherOver(
        IDiscoveryRunStore store,
        ExecutingDiscoveryRuns executing,
        ILogger<DiscoveryRunLauncher> logger)
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ => throw new InvalidOperationException("no scope was composed"));

        return new DiscoveryRunLauncher(
            scopeFactory,
            store,
            ClientSignalPublishers.ReachingNobody,
            executing,
            Substitute.For<IHostApplicationLifetime>(),
            new FakeTimeProvider(Now),
            logger);
    }
}

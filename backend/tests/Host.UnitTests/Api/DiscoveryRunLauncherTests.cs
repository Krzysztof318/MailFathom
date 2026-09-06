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
/// task it starts, so a fault escaping it would be observed by nobody and would leave a client watching a run that never
/// ends — and the use case deliberately publishes only the endings it can name, because <c>Application</c> has no logger
/// to write the rest down in. What is asserted here is the other half of that arrangement: the fault is written down,
/// the run is ended on it, and the registry is told, whichever of those the fault happened before.
/// </remarks>
public sealed class DiscoveryRunLauncherTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);

    private static readonly MailQuestion Question = new(
        MailQuestionText.Create("which supplier quoted least"),
        MailboxScope.Create(SyntheticMailOwner.Deployment, [], []));

    private readonly FakeTimeProvider clock = new(Now);

    /// <summary>A scope this process could not compose ends the run, rather than leaving a client watching one that never ends.</summary>
    [Fact]
    public async Task Start_AScopeThisProcessCannotCompose_EndsTheRunAsFailed()
    {
        // Arrange
        var registry = new DiscoveryRunRegistry(this.clock);
        registry.TryOpen(SyntheticMailOwner.Deployment, out var journal);
        Assert.NotNull(journal);

        // Act
        await LauncherOver(registry, new RecordingLogger<DiscoveryRunLauncher>()).Start(Question, journal, Caller);

        // Assert
        var published = await Published(journal);
        Assert.Equal(DiscoveryRunFailure.Failed, Assert.IsType<DiscoveryRunFailed>(published[^1]).Failure);
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
        var registry = new DiscoveryRunRegistry(this.clock);
        registry.TryOpen(SyntheticMailOwner.Deployment, out var journal);
        Assert.NotNull(journal);
        var logger = new RecordingLogger<DiscoveryRunLauncher>();

        // Act
        await LauncherOver(registry, logger).Start(Question, journal, Caller);

        // Assert
        var written = Assert.Single(logger.Messages);
        Assert.Contains("had no name for", written, StringComparison.Ordinal);
        Assert.DoesNotContain("supplier", written, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A run whose execution ended is held for its retention window from that moment, not from when it was opened.</summary>
    /// <remarks>
    /// Telling the registry is what starts that window, and it happens whichever way the run ended. So the clock is moved
    /// a whole window forward before the run is started: without that record the sweep on the next lookup would find a
    /// run last touched when it was opened and forget it, and a client reconnecting a second after a failure would be
    /// told there had never been such a run.
    /// </remarks>
    [Fact]
    public async Task Start_ARunThatFailed_IsStillHeldForItsWholeRetentionWindowAfterwards()
    {
        // Arrange
        var registry = new DiscoveryRunRegistry(this.clock);
        registry.TryOpen(SyntheticMailOwner.Deployment, out var journal);
        Assert.NotNull(journal);
        this.clock.Advance(DiscoveryRunBounds.RetentionAfterLastUse);

        // Act
        await LauncherOver(registry, new RecordingLogger<DiscoveryRunLauncher>()).Start(Question, journal, Caller);

        // Assert
        Assert.True(registry.TryFind(journal.Id, SyntheticMailOwner.Deployment, out _));
    }

    private static AuthorizedPrincipal Caller =>
        AuthorizedPrincipal.CallerActingFor(
            SyntheticMailOwner.Deployment,
            "test-caller",
            [MailFathomPermission.MailAsk]);

    private static DiscoveryRunLauncher LauncherOver(
        DiscoveryRunRegistry registry,
        ILogger<DiscoveryRunLauncher> logger)
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ => throw new InvalidOperationException("no scope was composed"));

        return new DiscoveryRunLauncher(
            scopeFactory,
            registry,
            Substitute.For<IHostApplicationLifetime>(),
            logger);
    }

    private static async Task<DiscoveryRunEvent[]> Published(DiscoveryRunJournal journal)
    {
        List<DiscoveryRunEvent> published = [];

        await foreach (var @event in journal.ReadFromAsync(afterSequence: 0, TestContext.Current.CancellationToken))
        {
            published.Add(@event);
        }

        return [.. published];
    }
}

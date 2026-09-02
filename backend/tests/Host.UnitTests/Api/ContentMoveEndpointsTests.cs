// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Persistence;
using MailFathom.Host.Api;
using MailFathom.Host.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers the four routes an operator moves stored content into the object backend through.</summary>
/// <remarks>
/// What is asserted here is what a deployment answers rather than what it copies: which verbs the paths carry, that a
/// deployment with nowhere to move its content refuses rather than starting a move that would carry nothing, and that a
/// decision about a move nobody asked for is a 404 rather than a move recorded to satisfy the request.
/// </remarks>
public sealed class ContentMoveEndpointsTests
{
    private static readonly DateTimeOffset Moment = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The deployment's half of an agreement with a command it cannot reference. <c>mfctl</c> composes all three paths
    /// from constants of its own, and a rename on either side compiles cleanly while the command reaches a 404 that
    /// reads exactly like an endpoint nobody enabled.
    /// </summary>
    [Fact]
    public void ContentMoveRoutes_AreThePathsTheCommandComposes()
    {
        Assert.Equal("/content/move", ContentMoveEndpoints.MoveRoute);
        Assert.Equal("/content/move/pause", ContentMoveEndpoints.PauseRoute);
        Assert.Equal("/content/move/resume", ContentMoveEndpoints.ResumeRoute);
    }

    /// <summary>The move is watched while it runs, which is one path answering two methods.</summary>
    [Fact]
    public void MapContentMove_TheMoveRoute_IsBothReadAndAskedFor()
    {
        // Arrange, Act
        var routes = MappedRoutes();

        // Assert
        Assert.Equal(
            ["GET", "POST"],
            routes
                .Where(route => route.RoutePattern.RawText == ContentMoveEndpoints.MoveRoute)
                .SelectMany(route => route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)
                .Order(StringComparer.Ordinal));
    }

    /// <summary>Stopping and starting again are opposite decisions, so each is a path taking one verb and no body.</summary>
    [Fact]
    public void MapContentMove_ThePauseAndResumeRoutes_EachAcceptOnlyAPost()
    {
        // Arrange, Act
        var routes = MappedRoutes();

        // Assert
        Assert.Equal(
            ["POST", "POST"],
            routes
                .Where(route => route.RoutePattern.RawText == ContentMoveEndpoints.PauseRoute
                    || route.RoutePattern.RawText == ContentMoveEndpoints.ResumeRoute)
                .SelectMany(route => route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods));
    }

    /// <summary>The backlog is what a switch is weighed against, so it is answered before any move exists.</summary>
    [Fact]
    public async Task ReadAsync_NoMoveYet_ReportsTheBacklogAndThatAMoveIsPossible()
    {
        // Arrange
        var runStore = Substitute.For<IStoredContentMoveRunStore>();
        var contentStore = ContentStoreHolding(payloadCount: 12, byteCount: 4_096);

        // Act
        var result = await ContentMoveEndpoints.ReadAsync(
            ControlOver(runStore),
            new StoredContentMoveReader(runStore, contentStore, AdministrativeGrant.WholeSurface),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((true, null, 12L, 4_096L), (
            result.Value!.Available,
            result.Value.Run,
            result.Value.RemainingPayloadCount,
            result.Value.RemainingByteCount));
    }

    /// <summary>A move is served by the names a client reads rather than by the ordinals the application holds.</summary>
    [Theory]
    [InlineData(StoredContentMoveState.Running, "running")]
    [InlineData(StoredContentMoveState.Paused, "paused")]
    [InlineData(StoredContentMoveState.Completed, "completed")]
    public async Task ReadAsync_AMoveUnderWay_NamesItsStateOnTheWire(StoredContentMoveState state, string expected)
    {
        // Arrange
        var runStore = RunStoreHolding(new StoredContentMoveRun
        {
            RequestedAt = Moment,
            State = state,
            Kind = EmailContentKind.IncomingMessage,
            CopiedPayloadCount = 12,
            FailedPayloadCount = 1,
            MovedByteCount = 4_096,
        });

        // Act
        var result = await ContentMoveEndpoints.ReadAsync(
            ControlOver(runStore),
            new StoredContentMoveReader(
                runStore,
                ContentStoreHolding(payloadCount: 0, byteCount: 0),
                AdministrativeGrant.WholeSurface),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((expected, Moment, 12L, 1L, 4_096L), (
            result.Value!.Run!.State,
            result.Value.Run.RequestedAt,
            result.Value.Run.CopiedPayloadCount,
            result.Value.Run.FailedPayloadCount,
            result.Value.Run.MovedByteCount));
    }

    /// <summary>A deployment storing its content in the database reads the backlog and is told a move is impossible.</summary>
    [Fact]
    public async Task ReadAsync_NoObjectBackendConfigured_StillAnswersButSaysAMoveIsNotPossible()
    {
        // Arrange
        var runStore = Substitute.For<IStoredContentMoveRunStore>();
        var contentStore = ContentStoreHolding(payloadCount: 3, byteCount: 300);

        // Act
        var result = await ContentMoveEndpoints.ReadAsync(
            ControlOver(runStore, withObjectBackend: false),
            new StoredContentMoveReader(runStore, contentStore, AdministrativeGrant.WholeSurface),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((false, 3L), (result.Value!.Available, result.Value.RemainingPayloadCount));
    }

    /// <summary>Asking a deployment with nowhere to move to is refused, and the refusal names the section that decides it.</summary>
    [Fact]
    public async Task StartAsync_NoObjectBackendConfigured_IsRefusedNamingTheConfiguration()
    {
        // Arrange
        var runStore = Substitute.For<IStoredContentMoveRunStore>();

        // Act
        var result = await ContentMoveEndpoints.StartAsync(
            ControlOver(runStore, withObjectBackend: false),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Contains("ContentStorage:ObjectStorage", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>The request records that a move is wanted and copies nothing, so it answers with the move it recorded.</summary>
    [Fact]
    public async Task StartAsync_ADeploymentWithABucket_AnswersWithTheMoveItRecorded()
    {
        // Arrange
        var runStore = Substitute.For<IStoredContentMoveRunStore>();

        // Act
        var result = await ContentMoveEndpoints.StartAsync(
            ControlOver(runStore),
            TestContext.Current.CancellationToken);

        // Assert
        var started = Assert.IsType<Ok<ContentMoveRunResponse>>(result.Result);
        Assert.Equal(("running", Moment, 0L), (
            started.Value!.State,
            started.Value.RequestedAt,
            started.Value.CopiedPayloadCount));
    }

    /// <summary>A deployment nobody asked for a move is told there is none, rather than being given one to stop.</summary>
    [Fact]
    public async Task PauseAsync_NoMoveYet_IsNotFound()
    {
        // Arrange
        var control = ControlOver(Substitute.For<IStoredContentMoveRunStore>());

        // Act
        var result = await ContentMoveEndpoints.PauseAsync(control, TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    /// <summary>A running move is stopped where it is, and the answer is the move as it now stands.</summary>
    [Fact]
    public async Task PauseAsync_AMoveRunning_AnswersWithItStopped()
    {
        // Arrange
        var control = ControlOver(RunStoreHolding(new StoredContentMoveRun
        {
            RequestedAt = Moment,
            State = StoredContentMoveState.Running,
            Kind = EmailContentKind.IncomingMessage,
            CopiedPayloadCount = 12,
        }));

        // Act
        var result = await ContentMoveEndpoints.PauseAsync(control, TestContext.Current.CancellationToken);

        // Assert
        var paused = Assert.IsType<Ok<ContentMoveRunResponse>>(result.Result);
        Assert.Equal(("paused", 12L), (paused.Value!.State, paused.Value.CopiedPayloadCount));
    }

    /// <summary>A deployment whose endpoint was taken away is refused rather than set going against nothing.</summary>
    [Fact]
    public async Task ResumeAsync_NoObjectBackendConfigured_IsRefusedNamingTheConfiguration()
    {
        // Arrange
        var control = ControlOver(Substitute.For<IStoredContentMoveRunStore>(), withObjectBackend: false);

        // Act
        var result = await ContentMoveEndpoints.ResumeAsync(control, TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
    }

    /// <summary>A stopped move is set going again from the position it stopped at.</summary>
    [Fact]
    public async Task ResumeAsync_AMovePaused_AnswersWithItRunningAgain()
    {
        // Arrange
        var control = ControlOver(RunStoreHolding(new StoredContentMoveRun
        {
            RequestedAt = Moment,
            State = StoredContentMoveState.Paused,
            Kind = EmailContentKind.OutgoingMessage,
            CopiedPayloadCount = 12,
        }));

        // Act
        var result = await ContentMoveEndpoints.ResumeAsync(control, TestContext.Current.CancellationToken);

        // Assert
        var resumed = Assert.IsType<Ok<ContentMoveRunResponse>>(result.Result);
        Assert.Equal("running", resumed.Value!.State);
    }

    private static IEnumerable<RouteEndpoint> MappedRoutes()
    {
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddLogging();

        var endpoints = new TestEndpointRouteBuilder(services.BuildServiceProvider());
        endpoints.MapGroup(string.Empty).MapContentMove();

        return
        [
            .. endpoints.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>(),
        ];
    }

    /// <summary>Answers with the backlog the test arranged, which is the one figure the reader asks the content for.</summary>
    private static IStoredContentMoveStore ContentStoreHolding(long payloadCount, long byteCount)
    {
        var contentStore = Substitute.For<IStoredContentMoveStore>();
        contentStore
            .CountPayloadsAwaitingMoveAsync(Arg.Any<CancellationToken>())
            .Returns(new StoredContentBacklog(payloadCount, byteCount));

        return contentStore;
    }

    /// <summary>Answers with the move the test arranged, and keeps whatever the control decides about it.</summary>
    private static IStoredContentMoveRunStore RunStoreHolding(StoredContentMoveRun arranged)
    {
        var runStore = Substitute.For<IStoredContentMoveRunStore>();
        var held = arranged;

        runStore.FindAsync(Arg.Any<CancellationToken>()).Returns(_ => held);
        runStore
            .SaveAsync(Arg.Any<IPersistenceSession>(), Arg.Any<StoredContentMoveRun>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                held = call.Arg<StoredContentMoveRun>();

                return Task.CompletedTask;
            });

        return runStore;
    }

    /// <summary>Builds the control the routes resolve, over a session factory that commits whatever it is handed.</summary>
    private static StoredContentMoveControl ControlOver(
        IStoredContentMoveRunStore runStore,
        bool withObjectBackend = true)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        FakeTimeProvider timeProvider = new(Moment);

        return new StoredContentMoveControl(
            runStore,
            new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), timeProvider),
            timeProvider,
            AdministrativeGrant.WholeSurface,
            withObjectBackend ? Substitute.For<IEmailContentObjectBackend>() : null);
    }

    /// <summary>Accepts every commit, because what the routes decide is asserted rather than how it is persisted.</summary>
    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

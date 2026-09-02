// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.EmailContent.Release;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Observability;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.Host.Security.Endpoints;
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

/// <summary>Covers the one route an operator ends the duplication on, and the two grants its two verbs are published under.</summary>
/// <remarks>
/// What is asserted here is what the deployment answers and under which permission, rather than which rows a batch
/// selects. The refusal is the part worth a route test of its own: a release asked for while the move is unfinished is a
/// conflict naming the backlog rather than a partial release of everything else.
/// </remarks>
public sealed class ContentReleaseEndpointsTests
{
    private static readonly DateTimeOffset Moment = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The deployment's half of an agreement with a command it cannot reference. <c>mfctl</c> composes the path from a
    /// constant of its own, and a rename on either side compiles cleanly while the command reaches a 404 that reads
    /// exactly like an endpoint nobody enabled.
    /// </summary>
    [Fact]
    public void ReleaseRoute_IsThePathTheCommandComposes()
    {
        Assert.Equal("/content/release", ContentReleaseEndpoints.ReleaseRoute);
    }

    /// <summary>The figure an operator confirms and the figure the deployment acts on are one path answering two verbs.</summary>
    [Fact]
    public void MapContentRelease_TheReleaseRoute_IsBothReadAndPerformed()
    {
        // Arrange, Act
        var routes = MappedRoutes();

        // Assert
        Assert.Equal(
            ["GET", "POST"],
            routes
                .SelectMany(route => route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)
                .Order(StringComparer.Ordinal));
    }

    /// <summary>Reading how much of a database is duplication is a reading; ending it is disposal, and they differ by grant.</summary>
    [Fact]
    public void MapContentRelease_TheTwoVerbs_AreServedUnderTheReadingAndTheErasingGrants()
    {
        // Arrange, Act
        var routes = MappedRoutes();

        // Assert
        Assert.Equal(
            [("GET", MailFathomPermission.AdminRead), ("POST", MailFathomPermission.AdminErase)],
            routes
                .Select(route => (
                    Method: route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single(),
                    Permission: route.Metadata.GetMetadata<RoutePermission>()!.Permission))
                .OrderBy(route => route.Method, StringComparer.Ordinal));
    }

    /// <summary>The two figures an operator weighs are answered on a deployment that has released nothing.</summary>
    [Fact]
    public async Task ReadAsync_CopiesRetainedAndContentStillAwaitingTheMove_ReportsBoth()
    {
        // Arrange
        var release = ReleaseOver(
            retained: new StoredContentBacklog(4, 8_192),
            awaitingMove: new StoredContentBacklog(2, 512));

        // Act
        var result = await ContentReleaseEndpoints.ReadAsync(release, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((0L, 4L, 8_192L, 2L), (
            result.Value!.ReleasedPayloadCount,
            result.Value.RetainedPayloadCount,
            result.Value.RetainedByteCount,
            result.Value.AwaitingMovePayloadCount));
    }

    /// <summary>One request frees one batch and answers with what is left, which is how the command knows to ask again.</summary>
    [Fact]
    public async Task ReleaseAsync_EverythingCarried_AnswersWithWhatItFreedAndWhatIsLeft()
    {
        // Arrange
        var releaseStore = Substitute.For<IRetainedContentReleaseStore>();
        releaseStore
            .CountRetainedPayloadsAsync(Arg.Any<CancellationToken>())
            .Returns(new StoredContentBacklog(2, 4_096));
        releaseStore
            .ReleaseAsync(
                Arg.Any<EmailContentKind>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(ReleasedContentPayloads.None);
        releaseStore
            .ReleaseAsync(
                EmailContentKind.IncomingMessage,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(new ReleasedContentPayloads(2, 4_096));

        // Act
        var result = await ContentReleaseEndpoints.ReleaseAsync(
            ReleaseOver(StoredContentBacklog.Empty, StoredContentBacklog.Empty, releaseStore),
            TestContext.Current.CancellationToken);

        // Assert
        var released = Assert.IsType<Ok<ContentReleaseResponse>>(result.Result);
        Assert.Equal((2L, 4_096L, 2L, 0L), (
            released.Value!.ReleasedPayloadCount,
            released.Value.ReleasedByteCount,
            released.Value.RetainedPayloadCount,
            released.Value.AwaitingMovePayloadCount));
    }

    /// <summary>An unfinished move refuses the whole request as a conflict, because asking again afterwards is right.</summary>
    [Fact]
    public async Task ReleaseAsync_ContentStillAwaitingTheMove_IsAConflictNamingTheBacklog()
    {
        // Arrange
        var release = ReleaseOver(
            retained: new StoredContentBacklog(4, 8_192),
            awaitingMove: new StoredContentBacklog(7, 2_048));

        // Act
        var result = await ContentReleaseEndpoints.ReleaseAsync(release, TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, refusal.StatusCode);
        Assert.Contains("7 payloads", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    private static IEnumerable<RouteEndpoint> MappedRoutes()
    {
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddLogging();

        var endpoints = new TestEndpointRouteBuilder(services.BuildServiceProvider());
        endpoints.MapGroup(string.Empty).MapContentRelease();

        return
        [
            .. endpoints.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>(),
        ];
    }

    /// <summary>Builds the use case the routes resolve, over stores that answer the two figures the test arranged.</summary>
    private static RetainedContentRelease ReleaseOver(
        StoredContentBacklog retained,
        StoredContentBacklog awaitingMove,
        IRetainedContentReleaseStore? releaseStore = null)
    {
        if (releaseStore is null)
        {
            releaseStore = Substitute.For<IRetainedContentReleaseStore>();
            releaseStore
                .CountRetainedPayloadsAsync(Arg.Any<CancellationToken>())
                .Returns(retained);
            releaseStore
                .ReleaseAsync(
                    Arg.Any<EmailContentKind>(),
                    Arg.Any<DateTimeOffset>(),
                    Arg.Any<int>(),
                    Arg.Any<CancellationToken>())
                .Returns(ReleasedContentPayloads.None);
        }

        var contentStore = Substitute.For<IStoredContentMoveStore>();
        contentStore.CountPayloadsAwaitingMoveAsync(Arg.Any<CancellationToken>()).Returns(awaitingMove);

        return new RetainedContentRelease(
            releaseStore,
            contentStore,
            Substitute.For<IRetainedContentReleaseTelemetry>(),
            new RetainedContentReleaseOptions(),
            new FakeTimeProvider(Moment),
            AdministrativeGrant.WholeSurface);
    }
}

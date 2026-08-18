// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Observability;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Endpoints;

/// <summary>Covers the one filter the administrative group carries, which is what a published decision costs a caller.</summary>
/// <remarks>
/// Three behaviors, and the third is the one that makes the arrangement fail closed. A route that decided a permission
/// admits the callers holding it and refuses the rest; a route that decided none admits everybody, including a
/// credential granted nothing; and a route that decided nothing at all is refused rather than served, so forgetting to
/// decide produces a route nobody reaches instead of a route everybody does.
/// </remarks>
public sealed class AdminRouteAuthorizationTests
{
    /// <summary>The pattern every route in this suite is mapped under, which is what a refusal is recorded as.</summary>
    private const string RoutePattern = "/api/admin/an-administrative-route";

    /// <summary>What the shared helper admits every test caller as, which is what a refusal names in a log.</summary>
    private const string CallerIdentity = "test-caller";

    /// <summary>The ordinary path: a caller holding what the route publishes reaches the handler and gets its answer.</summary>
    [Fact]
    public async Task RefuseUnpermittedAsync_ACallerHoldingWhatTheRoutePublishes_ReachesTheHandler()
    {
        // Arrange
        var context = ContextFor(
            AdminRoutePermission.Requiring(MailFathomPermission.AdminRead),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var answer = await AdminRouteAuthorization.RefuseUnpermittedAsync(context, _ => ValueTask.FromResult<object?>("served"));

        // Assert
        Assert.Equal("served", answer);
    }

    /// <summary>The refusal names the one permission that would have sufficed, and the caller never reaches the handler.</summary>
    [Fact]
    public async Task RefuseUnpermittedAsync_ACallerWithoutIt_RefusesNamingThatPermissionAlone()
    {
        // Arrange
        var reached = false;
        var context = ContextFor(
            AdminRoutePermission.Requiring(MailFathomPermission.AdminRead),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminOperate));

        // Act
        var answer = await AdminRouteAuthorization.RefuseUnpermittedAsync(
            context,
            _ =>
            {
                reached = true;

                return ValueTask.FromResult<object?>("served");
            });

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(answer);
        Assert.Equal(StatusCodes.Status403Forbidden, refusal.StatusCode);
        Assert.Contains(MailFathomPermission.AdminRead.Name, refusal.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(MailFathomPermission.AdminOperate.Name, refusal.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.False(reached);
    }

    /// <summary>The permission travels as its own member as well, so <c>mfctl</c> reads what to grant rather than parsing the sentence.</summary>
    [Fact]
    public async Task RefuseUnpermittedAsync_ACallerWithoutIt_CarriesThePermissionAsItsOwnMember()
    {
        // Arrange
        var context = ContextFor(
            AdminRoutePermission.Requiring(MailFathomPermission.AdminErase),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var answer = await AdminRouteAuthorization.RefuseUnpermittedAsync(context, _ => ValueTask.FromResult<object?>("served"));

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(answer);
        Assert.Equal(
            MailFathomPermission.AdminErase.Name,
            Assert.Contains(AdminRouteAuthorization.PermissionExtension, refusal.ProblemDetails.Extensions));
    }

    /// <summary>The session route's case: a credential granted nothing still reaches a route published under no permission.</summary>
    [Fact]
    public async Task RefuseUnpermittedAsync_ARouteRequiringNoPermission_ServesACallerGrantedNothing()
    {
        // Arrange
        var context = ContextFor(AdminRoutePermission.None, AccessAuthorizations.ForCallerGranted());

        // Act
        var answer = await AdminRouteAuthorization.RefuseUnpermittedAsync(context, _ => ValueTask.FromResult<object?>("served"));

        // Assert
        Assert.Equal("served", answer);
    }

    /// <summary>
    /// The regression the whole arrangement exists for. A route mapped into the group without deciding anything is
    /// refused rather than served, so a permission nobody remembered to write is a route nobody reaches.
    /// </summary>
    [Fact]
    public async Task RefuseUnpermittedAsync_ARouteThatDecidedNothing_RefusesEveryCaller()
    {
        // Arrange
        var reached = false;
        var context = ContextFor(
            publishedPermission: null,
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var answer = await AdminRouteAuthorization.RefuseUnpermittedAsync(
            context,
            _ =>
            {
                reached = true;

                return ValueTask.FromResult<object?>("served");
            });

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(answer);
        Assert.Equal(StatusCodes.Status403Forbidden, refusal.StatusCode);
        Assert.False(reached);
        Assert.DoesNotContain(AdminRouteAuthorization.PermissionExtension, refusal.ProblemDetails.Extensions);
    }

    /// <summary>
    /// The use case is the authority, and this is what its refusal looks like to a caller: the same shape as the
    /// transport's own, rather than an unhandled fault reported as a deployment that broke.
    /// </summary>
    [Fact]
    public async Task RefuseUnpermittedAsync_AUseCaseRefusingBehindAPermittedRoute_AnswersInTheSameShape()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead);
        var context = ContextFor(AdminRoutePermission.Requiring(MailFathomPermission.AdminRead), authorization);

        // Act
        var answer = await AdminRouteAuthorization.RefuseUnpermittedAsync(
            context,
            _ =>
            {
                authorization.RequirePermission(MailFathomPermission.AdminErase);

                return ValueTask.FromResult<object?>("served");
            });

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(answer);
        Assert.Equal(StatusCodes.Status403Forbidden, refusal.StatusCode);
        Assert.Equal(
            MailFathomPermission.AdminErase.Name,
            Assert.Contains(AdminRouteAuthorization.PermissionExtension, refusal.ProblemDetails.Extensions));
    }

    /// <summary>
    /// The neighbour of the case above, and the one that would fail open rather than closed: a route stating two
    /// decisions has no decision, and reading the last of them would let a second declaration — possibly the one
    /// requiring nothing — quietly replace what the route was published under.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RefuseUnpermittedAsync_ARouteThatDecidedTwice_RefusesEveryCallerWhicheverCameLast(
        bool lastRequiresNothing)
    {
        // Arrange
        var reached = false;
        var decisions = lastRequiresNothing
            ? new object[] { AdminRoutePermission.Requiring(MailFathomPermission.AdminRead), AdminRoutePermission.None }
            : [AdminRoutePermission.None, AdminRoutePermission.Requiring(MailFathomPermission.AdminRead)];

        var context = ContextFor(
            new EndpointMetadataCollection(decisions),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var answer = await AdminRouteAuthorization.RefuseUnpermittedAsync(
            context,
            _ =>
            {
                reached = true;

                return ValueTask.FromResult<object?>("served");
            });

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(answer);
        Assert.Equal(StatusCodes.Status403Forbidden, refusal.StatusCode);
        Assert.False(reached);
        Assert.DoesNotContain(AdminRouteAuthorization.PermissionExtension, refusal.ProblemDetails.Extensions);
    }

    /// <summary>The caller is told the permission, and the deployment is told which credential kept asking for it.</summary>
    [Fact]
    public async Task RefuseUnpermittedAsync_ACallerWithoutIt_IsRecordedNamingTheRouteAndThePermission()
    {
        // Arrange
        var refusals = Substitute.For<IAuthorizationRefusalTelemetry>();
        var context = ContextFor(
            AdminRoutePermission.Requiring(MailFathomPermission.AdminRead),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminOperate),
            refusals);

        // Act
        await AdminRouteAuthorization.RefuseUnpermittedAsync(context, _ => ValueTask.FromResult<object?>("served"));

        // Assert
        refusals.Received(1).RecordRefusal(
            ProtectedSurface.Administration,
            RoutePattern,
            MailFathomPermission.AdminRead,
            CallerIdentity);
    }

    /// <summary>A route no grant reaches is the refusal whose remedy is a defect report, so it is the last one to go uncounted.</summary>
    [Fact]
    public async Task RefuseUnpermittedAsync_ARouteThatDecidedNothing_IsRecordedNamingNoPermission()
    {
        // Arrange
        var refusals = Substitute.For<IAuthorizationRefusalTelemetry>();
        var context = ContextFor(
            publishedPermission: null,
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead),
            refusals);

        // Act
        await AdminRouteAuthorization.RefuseUnpermittedAsync(context, _ => ValueTask.FromResult<object?>("served"));

        // Assert
        refusals.Received(1).RecordRefusal(
            ProtectedSurface.Administration,
            RoutePattern,
            Arg.Is<MailFathomPermission>(static permission => !permission.IsSpecified),
            CallerIdentity);
    }

    /// <summary>The use case is the authority, so its refusal is recorded where it becomes the answer a caller receives.</summary>
    [Fact]
    public async Task RefuseUnpermittedAsync_AUseCaseRefusingBehindAPermittedRoute_IsRecordedOnce()
    {
        // Arrange
        var refusals = Substitute.For<IAuthorizationRefusalTelemetry>();
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead);
        var context = ContextFor(
            AdminRoutePermission.Requiring(MailFathomPermission.AdminRead),
            authorization,
            refusals);

        // Act
        await AdminRouteAuthorization.RefuseUnpermittedAsync(
            context,
            _ =>
            {
                authorization.RequirePermission(MailFathomPermission.AdminErase);

                return ValueTask.FromResult<object?>("served");
            });

        // Assert
        refusals.Received(1).RecordRefusal(
            ProtectedSurface.Administration,
            RoutePattern,
            MailFathomPermission.AdminErase,
            CallerIdentity);
    }

    /// <summary>A permitted request records nothing beyond what the existing request instrumentation already does.</summary>
    [Fact]
    public async Task RefuseUnpermittedAsync_ACallerHoldingWhatTheRoutePublishes_RecordsNoRefusal()
    {
        // Arrange
        var refusals = Substitute.For<IAuthorizationRefusalTelemetry>();
        var context = ContextFor(
            AdminRoutePermission.Requiring(MailFathomPermission.AdminRead),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead),
            refusals);

        // Act
        await AdminRouteAuthorization.RefuseUnpermittedAsync(context, _ => ValueTask.FromResult<object?>("served"));

        // Assert
        refusals.DidNotReceiveWithAnyArgs().RecordRefusal(default, default!, default, default);
    }

    /// <summary>Builds one request against a route carrying the decision named, or carrying none at all.</summary>
    private static EndpointFilterInvocationContext ContextFor(
        AdminRoutePermission? publishedPermission,
        AccessAuthorization authorization,
        IAuthorizationRefusalTelemetry? refusals = null) =>
        ContextFor(
            publishedPermission is null
                ? EndpointMetadataCollection.Empty
                : new EndpointMetadataCollection(publishedPermission),
            authorization,
            refusals);

    /// <summary>Builds one request against a route carrying exactly the metadata named.</summary>
    /// <remarks>
    /// The endpoint is a routed one because the route's own pattern is what a refusal is recorded under, and an
    /// endpoint without one would leave every test asserting the fallback rather than the name an operator reads.
    /// </remarks>
    private static EndpointFilterInvocationContext ContextFor(
        EndpointMetadataCollection metadata,
        AccessAuthorization authorization,
        IAuthorizationRefusalTelemetry? refusals = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(authorization);
        services.AddSingleton(refusals ?? Substitute.For<IAuthorizationRefusalTelemetry>());

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        httpContext.SetEndpoint(new RouteEndpoint(
            static _ => Task.CompletedTask,
            RoutePatternFactory.Parse(RoutePattern),
            order: 0,
            metadata,
            "an administrative route"));

        return EndpointFilterInvocationContext.Create(httpContext);
    }
}

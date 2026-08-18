// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>Builds one request against a route carrying the decision named, or carrying none at all.</summary>
    private static EndpointFilterInvocationContext ContextFor(
        AdminRoutePermission? publishedPermission,
        AccessAuthorization authorization) =>
        ContextFor(
            publishedPermission is null
                ? EndpointMetadataCollection.Empty
                : new EndpointMetadataCollection(publishedPermission),
            authorization);

    /// <summary>Builds one request against a route carrying exactly the metadata named.</summary>
    private static EndpointFilterInvocationContext ContextFor(
        EndpointMetadataCollection metadata,
        AccessAuthorization authorization)
    {
        var services = new ServiceCollection();
        services.AddSingleton(authorization);

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        httpContext.SetEndpoint(new Endpoint(requestDelegate: null, metadata, "an administrative route"));

        return EndpointFilterInvocationContext.Create(httpContext);
    }
}

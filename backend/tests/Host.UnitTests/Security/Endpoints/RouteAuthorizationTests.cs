// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Observability;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
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

/// <summary>Covers the one filter an HTTP route group carries, which is what a published decision costs a caller.</summary>
/// <remarks>
/// <para>
/// Four behaviors, and the last two are what make the arrangement fail closed. A route that decided a permission admits
/// the callers holding it and refuses the rest; a route that decided none admits everybody, including a credential
/// granted nothing; a route that decided nothing at all is refused rather than served; and a route that decided on a
/// permission belonging to the other half of the published set is refused too, because no credential the group admits
/// can carry that name.
/// </para>
/// <para>
/// Most of it is read through the administrative surface, which is where the arrangement began, and the cases where the
/// surface is the subject rather than the setting state it themselves.
/// </para>
/// </remarks>
public sealed class RouteAuthorizationTests
{
    /// <summary>The surface most of this suite reads the filter through, stated once so a case about the surface stands out.</summary>
    private const ProtectedSurface Surface = ProtectedSurface.Administration;

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
            RoutePermission.Requiring(MailFathomPermission.AdminRead),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var answer = await RouteAuthorization.RefuseUnpermittedAsync(context, Served, Surface);

        // Assert
        Assert.Equal("served", answer);
    }

    /// <summary>The same arrangement on the surface a client reaches, whose grants are drawn from the mailbox half.</summary>
    [Fact]
    public async Task RefuseUnpermittedAsync_ACallerHoldingWhatAClientRoutePublishes_ReachesTheHandler()
    {
        // Arrange
        var context = ContextFor(
            RoutePermission.Requiring(MailFathomPermission.MailRead),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

        // Act
        var answer = await RouteAuthorization.RefuseUnpermittedAsync(context, Served, ProtectedSurface.Mail);

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
            RoutePermission.Requiring(MailFathomPermission.AdminRead),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminOperate));

        // Act
        var answer = await RouteAuthorization.RefuseUnpermittedAsync(context, Reaching(() => reached = true), Surface);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(answer);
        Assert.Equal(StatusCodes.Status403Forbidden, refusal.StatusCode);
        Assert.Contains(MailFathomPermission.AdminRead.Name, refusal.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(MailFathomPermission.AdminOperate.Name, refusal.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.False(reached);
    }

    /// <summary>
    /// The client surface answers the same way, and that is the decision rather than an inheritance: its caller is a
    /// page holding this person's own credential, and the session route already tells that caller its whole grant — so
    /// naming what is missing from it discloses nothing the caller could not already read about itself.
    /// </summary>
    [Fact]
    public async Task RefuseUnpermittedAsync_AClientCallerWithoutIt_RefusesNamingThePermissionItLacks()
    {
        // Arrange
        var context = ContextFor(
            RoutePermission.Requiring(MailFathomPermission.MailRead),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailAsk));

        // Act
        var answer = await RouteAuthorization.RefuseUnpermittedAsync(context, Served, ProtectedSurface.Mail);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(answer);
        Assert.Equal(StatusCodes.Status403Forbidden, refusal.StatusCode);
        Assert.Equal(
            MailFathomPermission.MailRead.Name,
            Assert.Contains(RouteAuthorization.PermissionExtension, refusal.ProblemDetails.Extensions));
    }

    /// <summary>The permission travels as its own member as well, so <c>mfctl</c> reads what to grant rather than parsing the sentence.</summary>
    [Fact]
    public async Task RefuseUnpermittedAsync_ACallerWithoutIt_CarriesThePermissionAsItsOwnMember()
    {
        // Arrange
        var context = ContextFor(
            RoutePermission.Requiring(MailFathomPermission.AdminErase),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var answer = await RouteAuthorization.RefuseUnpermittedAsync(context, Served, Surface);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(answer);
        Assert.Equal(
            MailFathomPermission.AdminErase.Name,
            Assert.Contains(RouteAuthorization.PermissionExtension, refusal.ProblemDetails.Extensions));
    }

    /// <summary>The session route's case: a credential granted nothing still reaches a route published under no permission.</summary>
    [Fact]
    public async Task RefuseUnpermittedAsync_ARouteRequiringNoPermission_ServesACallerGrantedNothing()
    {
        // Arrange
        var context = ContextFor(RoutePermission.None, AccessAuthorizations.ForCallerGranted());

        // Act
        var answer = await RouteAuthorization.RefuseUnpermittedAsync(context, Served, Surface);

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
        var answer = await RouteAuthorization.RefuseUnpermittedAsync(context, Reaching(() => reached = true), Surface);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(answer);
        Assert.Equal(StatusCodes.Status403Forbidden, refusal.StatusCode);
        Assert.False(reached);
        Assert.DoesNotContain(RouteAuthorization.PermissionExtension, refusal.ProblemDetails.Extensions);
    }

    /// <summary>
    /// A route published under the other half's permission is the same defect wearing a decision: no credential this
    /// group admits can carry the name, so serving it would mean serving whoever the group let through.
    /// </summary>
    [Theory]
    [InlineData(ProtectedSurface.Administration)]
    [InlineData(ProtectedSurface.Mail)]
    public async Task RefuseUnpermittedAsync_ARouteDecidingOnTheOtherSurfacesPermission_RefusesEveryCaller(
        ProtectedSurface served)
    {
        // Arrange
        var reached = false;
        var otherHalf = served == ProtectedSurface.Mail
            ? MailFathomPermission.AdminRead
            : MailFathomPermission.MailRead;

        var context = ContextFor(
            RoutePermission.Requiring(otherHalf),
            AccessAuthorizations.ForCallerGranted(otherHalf));

        // Act
        var answer = await RouteAuthorization.RefuseUnpermittedAsync(context, Reaching(() => reached = true), served);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(answer);
        Assert.Equal(StatusCodes.Status403Forbidden, refusal.StatusCode);
        Assert.False(reached);
        Assert.DoesNotContain(RouteAuthorization.PermissionExtension, refusal.ProblemDetails.Extensions);
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
        var context = ContextFor(RoutePermission.Requiring(MailFathomPermission.AdminRead), authorization);

        // Act
        var answer = await RouteAuthorization.RefuseUnpermittedAsync(
            context,
            Refusing(authorization, MailFathomPermission.AdminErase),
            Surface);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(answer);
        Assert.Equal(StatusCodes.Status403Forbidden, refusal.StatusCode);
        Assert.Equal(
            MailFathomPermission.AdminErase.Name,
            Assert.Contains(RouteAuthorization.PermissionExtension, refusal.ProblemDetails.Extensions));
    }

    /// <summary>
    /// A deployment serving several owners is a state a start now admits, and an administrative act reached by a
    /// credential naming nobody has no single owner to be composed against. What the caller reads is the failure's own
    /// code and sentence rather than an unclassified fault, because no grant would have made the act answerable and
    /// the remedy is a credential that names its owner.
    /// </summary>
    [Fact]
    public async Task RefuseUnpermittedAsync_AUseCaseWithNoSoleOwnerToActFor_AnswersWithTheFailuresOwnCode()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead);
        var context = ContextFor(RoutePermission.Requiring(MailFathomPermission.AdminRead), authorization);

        // Act
        var answer = await RouteAuthorization.RefuseUnpermittedAsync(
            context,
            _ => throw DeploymentMailOwnerUnresolvedException.NoSoleOwnerToActFor(),
            Surface);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(answer);
        Assert.Equal(StatusCodes.Status409Conflict, refusal.StatusCode);
        Assert.Equal(
            MailFathomErrorCode.DeploymentMailOwnerUnresolved.Value,
            Assert.Contains(RouteAuthorization.ErrorCodeExtension, refusal.ProblemDetails.Extensions));
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
            ? new object[] { RoutePermission.Requiring(MailFathomPermission.AdminRead), RoutePermission.None }
            : [RoutePermission.None, RoutePermission.Requiring(MailFathomPermission.AdminRead)];

        var context = ContextFor(
            new EndpointMetadataCollection(decisions),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var answer = await RouteAuthorization.RefuseUnpermittedAsync(context, Reaching(() => reached = true), Surface);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(answer);
        Assert.Equal(StatusCodes.Status403Forbidden, refusal.StatusCode);
        Assert.False(reached);
        Assert.DoesNotContain(RouteAuthorization.PermissionExtension, refusal.ProblemDetails.Extensions);
    }

    /// <summary>The caller is told the permission, and the deployment is told which credential kept asking for it.</summary>
    [Fact]
    public async Task RefuseUnpermittedAsync_ACallerWithoutIt_IsRecordedNamingTheRouteAndThePermission()
    {
        // Arrange
        var refusals = Substitute.For<IAuthorizationRefusalTelemetry>();
        var context = ContextFor(
            RoutePermission.Requiring(MailFathomPermission.AdminRead),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminOperate),
            refusals);

        // Act
        await RouteAuthorization.RefuseUnpermittedAsync(context, Served, Surface);

        // Assert
        refusals.Received(1).RecordRefusal(
            ProtectedSurface.Administration,
            RoutePattern,
            MailFathomPermission.AdminRead,
            CallerIdentity);
    }

    /// <summary>A refusal is counted against the surface the group serves, so one surface's rate is never read as another's.</summary>
    [Fact]
    public async Task RefuseUnpermittedAsync_AClientCallerWithoutIt_IsRecordedAgainstTheMailSurface()
    {
        // Arrange
        var refusals = Substitute.For<IAuthorizationRefusalTelemetry>();
        var context = ContextFor(
            RoutePermission.Requiring(MailFathomPermission.MailRead),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailAsk),
            refusals);

        // Act
        await RouteAuthorization.RefuseUnpermittedAsync(context, Served, ProtectedSurface.Mail);

        // Assert
        refusals.Received(1).RecordRefusal(
            ProtectedSurface.Mail,
            RoutePattern,
            MailFathomPermission.MailRead,
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
        await RouteAuthorization.RefuseUnpermittedAsync(context, Served, Surface);

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
            RoutePermission.Requiring(MailFathomPermission.AdminRead),
            authorization,
            refusals);

        // Act
        await RouteAuthorization.RefuseUnpermittedAsync(
            context,
            Refusing(authorization, MailFathomPermission.AdminErase),
            Surface);

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
            RoutePermission.Requiring(MailFathomPermission.AdminRead),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead),
            refusals);

        // Act
        await RouteAuthorization.RefuseUnpermittedAsync(context, Served, Surface);

        // Assert
        refusals.DidNotReceiveWithAnyArgs().RecordRefusal(default, default!, default, default);
    }

    /// <summary>The handler behind the filter, which answers whenever the filter lets a request reach it.</summary>
    private static ValueTask<object?> Served(EndpointFilterInvocationContext context) =>
        ValueTask.FromResult<object?>("served");

    /// <summary>A handler that reports having been reached, which is how a refusal is told from a served request.</summary>
    private static EndpointFilterDelegate Reaching(Action onReached) =>
        _ =>
        {
            onReached();

            return ValueTask.FromResult<object?>("served");
        };

    /// <summary>A handler standing in for a use case that refuses over a permission of its own.</summary>
    private static EndpointFilterDelegate Refusing(AccessAuthorization authorization, MailFathomPermission required) =>
        _ =>
        {
            authorization.RequirePermission(required);

            return ValueTask.FromResult<object?>("served");
        };

    /// <summary>Builds one request against a route carrying the decision named, or carrying none at all.</summary>
    private static EndpointFilterInvocationContext ContextFor(
        RoutePermission? publishedPermission,
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

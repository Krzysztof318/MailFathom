// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using MailFathom.Application.Access;
using MailFathom.Application.Access.Grants;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Hosting.Startup;
using MailFathom.Host.Security.ApiKeys;
using MailFathom.Host.Security.Transport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the session read tells a caller about itself, which is the one question every caller may ask.</summary>
/// <remarks>
/// The route requires no permission, so what it answers is what a credential granted nothing sees. Reporting the grant
/// there is what lets an operator read what they hold instead of discovering it one refusal at a time, and what makes a
/// credential retired by narrowing its entry to nothing distinguishable from one that still works.
/// </remarks>
public sealed class AdminSessionResponseTests
{
    private static readonly UserId Administrator = UserId.Create(Guid.Parse("0199a1b2-0000-7000-8000-0000000ad001"));

    [Fact]
    public void For_ACallerWithAGrant_ReportsEveryPermissionItHolds()
    {
        // Arrange
        var principal = AuthorizedPrincipal.Caller(
            "workstation",
            [MailFathomPermission.AdminOperate, MailFathomPermission.AdminRead]);

        // Act
        var session = AdminSessionResponse.For(new ClaimsPrincipal(new ClaimsIdentity()), principal, user: null, roles: []);

        // Assert
        Assert.Equal(
            [MailFathomPermission.AdminRead.Name, MailFathomPermission.AdminOperate.Name],
            session.Permissions);
    }

    /// <summary>
    /// The published order rather than the grant's own, so two credentials granted the same permissions read
    /// identically whichever order an operator happened to write them in.
    /// </summary>
    [Fact]
    public void For_TwoGrantsWrittenInDifferentOrders_ReportsThemIdentically()
    {
        // Arrange
        var caller = new ClaimsPrincipal(new ClaimsIdentity());

        // Act
        var first = AdminSessionResponse.For(
            caller,
            AuthorizedPrincipal.Caller("one", [MailFathomPermission.AdminRead, MailFathomPermission.AdminErase]),
            user: null,
            roles: []);
        var second = AdminSessionResponse.For(
            caller,
            AuthorizedPrincipal.Caller("two", [MailFathomPermission.AdminErase, MailFathomPermission.AdminRead]),
            user: null,
            roles: []);

        // Assert
        Assert.Equal(first.Permissions, second.Permissions);
    }

    /// <summary>
    /// An organization's administrator holds nothing over the whole deployment, so the permissions read empty and the
    /// scopes are where they read what they are granted at each scope — the deployment first, then each organization.
    /// </summary>
    [Fact]
    public void For_ACallerHoldingNamesAtSeveralScopes_ReportsEachScopeWithWhatItHoldsThere()
    {
        // Arrange
        var organization = new Guid("0198f0aa-0000-7000-8000-0000000000b1");
        var principal = AuthorizedPrincipal.Caller(
            "organization-administrator",
            ScopedGrant.Of([
                (MailFathomPermission.AdminOperate, AssignmentScope.Organization(organization)),
                (MailFathomPermission.AdminRead, AssignmentScope.Organization(organization)),
                (MailFathomPermission.AdminAuditRead, AssignmentScope.Deployment),
            ]));

        // Act
        var session = AdminSessionResponse.For(new ClaimsPrincipal(new ClaimsIdentity()), principal, user: null, roles: []);

        // Assert
        Assert.Equal([MailFathomPermission.AdminAuditRead.Name], session.Permissions);
        Assert.Equal(
            [
                ("deployment", (Guid?)null, MailFathomPermission.AdminAuditRead.Name),
                ("organization", organization, $"{MailFathomPermission.AdminRead.Name} {MailFathomPermission.AdminOperate.Name}"),
            ],
            session.Scopes.Select(scope => (scope.Scope, scope.Target, string.Join(' ', scope.Permissions))));
    }

    /// <summary>
    /// A role is assigned whole, so the spending name may be held over an organization, where no operation it covers
    /// exists. It is reported apart rather than among what the scope reaches, so nobody reads it as a grant the
    /// deployment fails to enforce.
    /// </summary>
    [Fact]
    public void For_TheSpendingPermissionHeldBelowTheDeployment_ReportsItAsReachingNothingThere()
    {
        // Arrange
        var organization = AssignmentScope.Organization(new Guid("0198f0aa-0000-7000-8000-0000000000b2"));
        var principal = AuthorizedPrincipal.Caller(
            "organization-administrator",
            ScopedGrant.Of([
                (MailFathomPermission.AdminSpend, organization),
                (MailFathomPermission.AdminRead, organization),
                (MailFathomPermission.AdminSpend, AssignmentScope.Deployment),
            ]));

        // Act
        var session = AdminSessionResponse.For(new ClaimsPrincipal(new ClaimsIdentity()), principal, user: null, roles: []);

        // Assert
        var deployment = session.Scopes.Single(scope => scope.Target is null);
        var narrower = session.Scopes.Single(scope => scope.Target is not null);
        Assert.Equal([MailFathomPermission.AdminSpend.Name], deployment.Permissions);
        Assert.Empty(deployment.ReachingNothing);
        Assert.Equal([MailFathomPermission.AdminRead.Name], narrower.Permissions);
        Assert.Equal([MailFathomPermission.AdminSpend.Name], narrower.ReachingNothing);
    }

    /// <summary>
    /// An administrator's grant keeps the mail names its user holds, as the ceiling a credential it writes is bounded
    /// by. None of them is something this endpoint performs, so neither reading reports one.
    /// </summary>
    [Fact]
    public void For_AnAdministratorWhoseGrantKeepsMailNames_ReportsTheAdministrativeNamesAlone()
    {
        // Arrange
        var principal = AuthorizedPrincipal.Caller(
            "administrator",
            ScopedGrant.Of([
                (MailFathomPermission.MailRead, AssignmentScope.Deployment),
                (MailFathomPermission.AdminRead, AssignmentScope.Deployment),
            ]));

        // Act
        var session = AdminSessionResponse.For(new ClaimsPrincipal(new ClaimsIdentity()), principal, user: null, roles: []);

        // Assert
        Assert.Equal([MailFathomPermission.AdminRead.Name], session.Permissions);
        Assert.Equal([MailFathomPermission.AdminRead.Name], Assert.Single(session.Scopes).Permissions);
    }

    /// <summary>A credential granted nothing reaches this route and nowhere else, and "nothing" is the accurate answer.</summary>
    [Fact]
    public void For_ACallerGrantedNothing_ReportsAnEmptyGrantRatherThanFailing()
    {
        // Act
        var session = AdminSessionResponse.For(
            new ClaimsPrincipal(new ClaimsIdentity()),
            AuthorizedPrincipal.Caller("retired", []),
            user: null,
            roles: []);

        // Assert
        Assert.Empty(session.Permissions);
        Assert.Empty(session.Scopes);
    }

    /// <summary>A request that established no principal is answered rather than faulted, for the same reason.</summary>
    [Fact]
    public void For_ARequestThatEstablishedNoPrincipal_ReportsAnEmptyGrant()
    {
        // Act
        var session = AdminSessionResponse.For(new ClaimsPrincipal(new ClaimsIdentity()), principal: null, user: null, roles: []);

        // Assert
        Assert.Empty(session.Permissions);
        Assert.Empty(session.Scopes);
    }

    /// <summary>A user who signed in is told who they are and which roles they hold where, read from their own records.</summary>
    [Fact]
    public async Task ReadSessionAsync_AnAdministratorSignedInWithTheirCredential_ReportsTheirUserAndTheRolesTheyHold()
    {
        // Arrange
        var context = new DefaultHttpContext
        {
            User = AuthenticatedAdministrator(Administrator),
            Request = { Path = AdminEndpointOptions.RoutePrefix + AdminApiEndpoints.SessionRoute },
        };
        var users = Substitute.For<IUserDirectory>();
        users.ReadUserAsync(Administrator, Arg.Any<CancellationToken>()).Returns(new UserRecord(Administrator, "Ada Admin"));
        var grants = Substitute.For<IGrantStore>();
        grants.ReadRolesHeldByAsync(Administrator, Arg.Any<CancellationToken>())
            .Returns([new HeldRole("administrator", AssignmentScope.Deployment)]);

        // Act
        var answer = await AdminApiEndpoints.ReadSessionAsync(
            context,
            PrincipalsOver(context, administratorConfiguresACredential: true),
            users,
            grants,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new AdminSessionUserResponse(Administrator.Value, "Ada Admin"), answer.Value?.User);
        Assert.Equal([new AdminSessionRoleResponse("administrator", "deployment", null)], answer.Value?.Roles);
    }

    /// <summary>A request admitted as nobody names no user and asks for no roles, because there is nobody to ask about.</summary>
    [Fact]
    public async Task ReadSessionAsync_ARequestAdmittedAsNobody_ReportsNoUserAndNoRoles()
    {
        // Arrange
        var context = new DefaultHttpContext
        {
            Request = { Path = AdminEndpointOptions.RoutePrefix + AdminApiEndpoints.SessionRoute },
        };
        var grants = Substitute.For<IGrantStore>();

        // Act
        var answer = await AdminApiEndpoints.ReadSessionAsync(
            context,
            PrincipalsOver(context, administratorConfiguresACredential: true),
            Substitute.For<IUserDirectory>(),
            grants,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(answer.Value);
        Assert.Null(answer.Value.User);
        Assert.Empty(answer.Value.Roles);
        await grants.DidNotReceive().ReadRolesHeldByAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A role is reported with how far it reaches, and a narrower scope names what it is narrowed to.</summary>
    [Fact]
    public void For_ARoleHeldAtEachScope_ReportsTheScopeAndWhatItNames()
    {
        // Arrange
        var organization = Guid.Parse("0199a1b2-0000-7000-8000-00000000c0de");
        var user = UserId.Create(Guid.Parse("0199a1b2-0000-7000-8000-0000000ad001"));

        // Act
        AdminSessionRoleResponse[] reported =
        [
            AdminSessionRoleResponse.For(new HeldRole("administrator", AssignmentScope.Deployment)),
            AdminSessionRoleResponse.For(new HeldRole("auditor", AssignmentScope.Organization(organization))),
            AdminSessionRoleResponse.For(new HeldRole("delegate", AssignmentScope.User(user))),
        ];

        // Assert
        Assert.Equal(
            [
                new AdminSessionRoleResponse("administrator", "deployment", null),
                new AdminSessionRoleResponse("auditor", "organization", organization),
                new AdminSessionRoleResponse("delegate", "user", user.Value),
            ],
            reported);
    }

    /// <summary>Composes the principal source over one request, as the composed host does for the administrative surface.</summary>
    private static TransportAuthorizedPrincipalSource PrincipalsOver(HttpContext context, bool administratorConfiguresACredential)
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(context);

        var adminEndpoint = new AdminEndpointOptions();
        if (administratorConfiguresACredential)
        {
            adminEndpoint.Authentication.Add(new UserFacingAuthenticationOptions());
        }

        return new TransportAuthorizedPrincipalSource(
            httpContextAccessor,
            Substitute.For<IDeploymentUserSource>(),
            Options.Create(new McpEndpointOptions()),
            Options.Create(adminEndpoint),
            Options.Create(new ClientEndpointOptions()),
            new RecordedDefaultAdministrator());
    }

    /// <summary>Composes the principal a user's own credential produces on the administrative surface.</summary>
    private static ClaimsPrincipal AuthenticatedAdministrator(UserId user) =>
        new(new ClaimsIdentity(
            [
                new Claim(ApiKeyAuthentication.ApiKeyNameClaimType, "admin-key"),
                TransportCallerUser.ClaimFor(user),
                .. TransportGrant.ClaimsFor([MailFathomPermission.AdminRead]),
            ],
            "test",
            ApiKeyAuthentication.ApiKeyNameClaimType,
            ApiKeyAuthentication.RoleClaimType));
}

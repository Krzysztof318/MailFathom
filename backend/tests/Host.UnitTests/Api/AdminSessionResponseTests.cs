// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
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
    [Fact]
    public void For_ACallerWithAGrant_ReportsEveryPermissionItHolds()
    {
        // Arrange
        var principal = AuthorizedPrincipal.Caller(
            "workstation",
            [MailFathomPermission.AdminOperate, MailFathomPermission.AdminRead]);

        // Act
        var session = AdminSessionResponse.For(new ClaimsPrincipal(new ClaimsIdentity()), principal);

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
            AuthorizedPrincipal.Caller("one", [MailFathomPermission.AdminRead, MailFathomPermission.AdminErase]));
        var second = AdminSessionResponse.For(
            caller,
            AuthorizedPrincipal.Caller("two", [MailFathomPermission.AdminErase, MailFathomPermission.AdminRead]));

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
        var session = AdminSessionResponse.For(new ClaimsPrincipal(new ClaimsIdentity()), principal);

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
        var session = AdminSessionResponse.For(new ClaimsPrincipal(new ClaimsIdentity()), principal);

        // Assert
        var deployment = session.Scopes.Single(scope => scope.Target is null);
        var narrower = session.Scopes.Single(scope => scope.Target is not null);
        Assert.Equal([MailFathomPermission.AdminSpend.Name], deployment.Permissions);
        Assert.Empty(deployment.ReachingNothing);
        Assert.Equal([MailFathomPermission.AdminRead.Name], narrower.Permissions);
        Assert.Equal([MailFathomPermission.AdminSpend.Name], narrower.ReachingNothing);
    }

    /// <summary>A credential granted nothing reaches this route and nowhere else, and "nothing" is the accurate answer.</summary>
    [Fact]
    public void For_ACallerGrantedNothing_ReportsAnEmptyGrantRatherThanFailing()
    {
        // Act
        var session = AdminSessionResponse.For(
            new ClaimsPrincipal(new ClaimsIdentity()),
            AuthorizedPrincipal.Caller("retired", []));

        // Assert
        Assert.Empty(session.Permissions);
        Assert.Empty(session.Scopes);
    }

    /// <summary>A request that established no principal is answered rather than faulted, for the same reason.</summary>
    [Fact]
    public void For_ARequestThatEstablishedNoPrincipal_ReportsAnEmptyGrant()
    {
        // Act
        var session = AdminSessionResponse.For(new ClaimsPrincipal(new ClaimsIdentity()), principal: null);

        // Assert
        Assert.Empty(session.Permissions);
        Assert.Empty(session.Scopes);
    }
}

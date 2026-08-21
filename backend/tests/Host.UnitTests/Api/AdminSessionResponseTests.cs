// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
    }

    /// <summary>A request that established no principal is answered rather than faulted, for the same reason.</summary>
    [Fact]
    public void For_ARequestThatEstablishedNoPrincipal_ReportsAnEmptyGrant()
    {
        // Act
        var session = AdminSessionResponse.For(new ClaimsPrincipal(new ClaimsIdentity()), principal: null);

        // Assert
        Assert.Empty(session.Permissions);
    }
}

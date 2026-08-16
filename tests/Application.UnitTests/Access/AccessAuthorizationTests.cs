// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Access;

/// <summary>Covers what a use case is told when it asks who reached it.</summary>
/// <remarks>
/// Every test here runs with no transport of any kind, which is the point: the transport refuses cheaply and this is
/// the authority, so an entrypoint that never passed any middleware has to meet the same answer.
/// </remarks>
public sealed class AccessAuthorizationTests
{
    private const string ConfiguredCredentialName = "mcp-key";

    [Fact]
    public void RequirePermission_CallerGrantedIt_Permits()
    {
        // Arrange
        var authorization = AuthorizationOver(
            AuthorizedPrincipal.Caller(ConfiguredCredentialName, [MailFathomPermission.MailRead]));

        // Act
        var refusal = Record.Exception(() => authorization.RequirePermission(MailFathomPermission.MailRead));

        // Assert
        Assert.Null(refusal);
    }

    /// <summary>No permission implies another, so holding one of a surface's names says nothing about holding the next.</summary>
    [Fact]
    public void RequirePermission_CallerGrantedADifferentPermission_RefusesNamingWhatWouldHaveSufficed()
    {
        // Arrange
        var authorization = AuthorizationOver(
            AuthorizedPrincipal.Caller(ConfiguredCredentialName, [MailFathomPermission.MailRead]));

        // Act
        var refusal = Assert.Throws<PrincipalNotAuthorizedException>(() =>
            authorization.RequirePermission(MailFathomPermission.MailAsk));

        // Assert
        Assert.Equal(MailFathomPermission.MailAsk, refusal.RequiredPermission);
        Assert.Equal(MailFathomErrorCode.PrincipalNotAuthorized, refusal.ErrorCode);
    }

    /// <summary>An entry an operator emptied grants nothing, and a credential it admits is refused every operation.</summary>
    [Fact]
    public void RequirePermission_CallerGrantedNothing_Refuses()
    {
        // Arrange
        var authorization = AuthorizationOver(AuthorizedPrincipal.Caller(ConfiguredCredentialName, []));

        // Act & Assert
        Assert.Throws<PrincipalNotAuthorizedException>(() =>
            authorization.RequirePermission(MailFathomPermission.MailRead));
    }

    /// <summary>
    /// The process identity must never be admitted by holding a permission, because a principal that passes an ordinary
    /// check is a caller with everything granted wearing a different label.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryPublishedPermission))]
    public void RequirePermission_ProcessIdentity_RefusesWhicheverPermissionIsAsked(MailFathomPermission permission)
    {
        // Arrange
        var authorization = AuthorizationOver(AuthorizedPrincipal.Process);

        // Act & Assert
        Assert.Throws<PrincipalNotAuthorizedException>(() => authorization.RequirePermission(permission));
    }

    /// <summary>A capability is bounded to one object, so it is never a way to reach an operation published over a surface.</summary>
    [Fact]
    public void RequirePermission_SignedCapability_Refuses()
    {
        // Arrange
        var authorization = AuthorizationOver(AuthorizedPrincipal.SignedCapability("/attachments/an-object/0"));

        // Act & Assert
        Assert.Throws<PrincipalNotAuthorizedException>(() =>
            authorization.RequirePermission(MailFathomPermission.MailRead));
    }

    [Fact]
    public void RequireProcessIdentity_WorkNoCallerRequested_Permits()
    {
        // Arrange
        var authorization = AuthorizationOver(AuthorizedPrincipal.Process);

        // Act
        var refusal = Record.Exception(authorization.RequireProcessIdentity);

        // Assert
        Assert.Null(refusal);
    }

    /// <summary>
    /// Work the process runs for itself is not something a credential reaches, whatever that credential was granted, so
    /// the refusal names no permission for an operator to add.
    /// </summary>
    [Fact]
    public void RequireProcessIdentity_ACallerGrantedEverything_RefusesNamingNoPermission()
    {
        // Arrange
        var authorization = AuthorizationOver(
            AuthorizedPrincipal.Caller(ConfiguredCredentialName, MailFathomPermission.All));

        // Act
        var refusal = Assert.Throws<PrincipalNotAuthorizedException>(authorization.RequireProcessIdentity);

        // Assert
        Assert.False(refusal.RequiredPermission.IsSpecified);
    }

    [Fact]
    public void RequireSignedCapability_AVerifiedCapability_Permits()
    {
        // Arrange
        var authorization = AuthorizationOver(AuthorizedPrincipal.SignedCapability("/attachments/an-object/0"));

        // Act
        var refusal = Record.Exception(authorization.RequireSignedCapability);

        // Assert
        Assert.Null(refusal);
    }

    [Fact]
    public void RequireSignedCapability_TheProcessIdentity_Refuses()
    {
        // Arrange
        var authorization = AuthorizationOver(AuthorizedPrincipal.Process);

        // Act & Assert
        Assert.Throws<PrincipalNotAuthorizedException>(authorization.RequireSignedCapability);
    }

    /// <summary>
    /// The case an entrypoint added later produces by omission: nothing said what admitted the work. Every requirement
    /// refuses it, which is what "fails rather than defaulting to permitted" means in the one place it is decided.
    /// </summary>
    [Fact]
    public void EveryRequirement_ReachedUnderNoPrincipal_Refuses()
    {
        // Arrange
        var authorization = AuthorizationOver(principal: null);

        // Act
        var refusals = new Action[]
        {
            () => authorization.RequirePermission(MailFathomPermission.MailRead),
            authorization.RequireProcessIdentity,
            authorization.RequireSignedCapability,
        }.Select(requirement => Record.Exception(requirement));

        // Assert
        Assert.All(refusals, refusal => Assert.IsType<PrincipalNotAuthorizedException>(refusal));
    }

    /// <summary>A use case requiring the struct default would be requiring nothing, which is a defect in it rather than a caller's refusal.</summary>
    [Fact]
    public void RequirePermission_TheUnspecifiedPermission_IsRejectedAsAnArgument()
    {
        // Arrange
        var authorization = AuthorizationOver(
            AuthorizedPrincipal.Caller(ConfiguredCredentialName, MailFathomPermission.All));

        // Act & Assert
        Assert.Throws<ArgumentException>(() => authorization.RequirePermission(default));
    }

    public static TheoryData<MailFathomPermission> EveryPublishedPermission() =>
        [.. MailFathomPermission.All];

    private static AccessAuthorization AuthorizationOver(AuthorizedPrincipal? principal)
    {
        var principals = Substitute.For<IAuthorizedPrincipalSource>();
        principals.Current.Returns(principal);

        return new AccessAuthorization(principals);
    }
}

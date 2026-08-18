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

    /// <summary>An act two surfaces perform is reached from either half, which is what the alternative exists for.</summary>
    [Theory]
    [InlineData(nameof(MailFathomPermission.AdminOperate))]
    [InlineData(nameof(MailFathomPermission.MailContactsWrite))]
    public void RequireAnyPermission_CallerGrantedEitherAlternative_Permits(string granted)
    {
        // Arrange
        var held = granted == nameof(MailFathomPermission.AdminOperate)
            ? MailFathomPermission.AdminOperate
            : MailFathomPermission.MailContactsWrite;

        var authorization = AuthorizationOver(AuthorizedPrincipal.Caller(ConfiguredCredentialName, [held]));

        // Act
        var refusal = Record.Exception(() => authorization.RequireAnyPermission(
            MailFathomPermission.AdminOperate,
            MailFathomPermission.MailContactsWrite));

        // Assert
        Assert.Null(refusal);
    }

    /// <summary>An alternative is not a widening: a grant holding neither name is refused exactly as one name would refuse it.</summary>
    [Fact]
    public void RequireAnyPermission_CallerGrantedNeitherAlternative_Refuses()
    {
        // Arrange
        var authorization = AuthorizationOver(
            AuthorizedPrincipal.Caller(ConfiguredCredentialName, [MailFathomPermission.MailContactsRead]));

        // Act
        var refusal = Assert.Throws<PrincipalNotAuthorizedException>(() => authorization.RequireAnyPermission(
            MailFathomPermission.AdminOperate,
            MailFathomPermission.MailContactsWrite));

        // Assert
        Assert.Equal(MailFathomErrorCode.PrincipalNotAuthorized, refusal.ErrorCode);
    }

    /// <summary>An operator diagnosing a refusal needs the name they could have granted, not the one from the half this caller cannot reach.</summary>
    [Fact]
    public void RequireAnyPermission_CallerGrantedNeither_RefusesNamingTheAlternativeOnItsOwnSurface()
    {
        // Arrange
        var authorization = AuthorizationOver(
            AuthorizedPrincipal.Caller(ConfiguredCredentialName, [MailFathomPermission.MailContactsRead]));

        // Act
        var refusal = Assert.Throws<PrincipalNotAuthorizedException>(() => authorization.RequireAnyPermission(
            MailFathomPermission.AdminOperate,
            MailFathomPermission.MailContactsWrite));

        // Assert
        Assert.Equal(MailFathomPermission.MailContactsWrite, refusal.RequiredPermission);
    }

    /// <summary>A caller granted nothing has no surface to read, so the refusal names the alternative the use case listed first.</summary>
    [Fact]
    public void RequireAnyPermission_CallerGrantedNothing_RefusesNamingTheFirstAlternative()
    {
        // Arrange
        var authorization = AuthorizationOver(AuthorizedPrincipal.Caller(ConfiguredCredentialName, []));

        // Act
        var refusal = Assert.Throws<PrincipalNotAuthorizedException>(() => authorization.RequireAnyPermission(
            MailFathomPermission.AdminOperate,
            MailFathomPermission.MailContactsWrite));

        // Assert
        Assert.Equal(MailFathomPermission.AdminOperate, refusal.RequiredPermission);
    }

    /// <summary>Work no caller requested is admitted by its kind rather than by a grant, so holding a name is not what reaches this.</summary>
    [Fact]
    public void RequireAnyPermission_ProcessIdentity_RefusesBothAlternatives()
    {
        // Arrange
        var authorization = AuthorizationOver(AuthorizedPrincipal.Process);

        // Act, Assert
        Assert.Throws<PrincipalNotAuthorizedException>(() => authorization.RequireAnyPermission(
            MailFathomPermission.AdminOperate,
            MailFathomPermission.MailContactsWrite));
    }

    /// <summary>The unspecified default names no act, so listing it as an alternative is a defect in the use case rather than a refusal.</summary>
    [Fact]
    public void RequireAnyPermission_AnUnspecifiedAlternative_IsRejectedAsAnArgument()
    {
        // Arrange
        var authorization = AuthorizationOver(
            AuthorizedPrincipal.Caller(ConfiguredCredentialName, [MailFathomPermission.AdminOperate]));

        // Act, Assert
        Assert.Throws<ArgumentException>(() => authorization.RequireAnyPermission(
            MailFathomPermission.AdminOperate,
            default));
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

        // Act
        var refusal = Assert.Throws<PrincipalNotAuthorizedException>(() =>
            authorization.RequirePermission(permission));

        // Assert
        Assert.False(refusal.RequiredPermission.IsSpecified);
    }

    /// <summary>A capability is bounded to one object, so it is never a way to reach an operation published over a surface.</summary>
    [Fact]
    public void RequirePermission_SignedCapability_RefusesNamingNoPermission()
    {
        // Arrange
        var authorization = AuthorizationOver(AuthorizedPrincipal.SignedCapability("/attachments/an-object/0"));

        // Act
        var refusal = Assert.Throws<PrincipalNotAuthorizedException>(() =>
            authorization.RequirePermission(MailFathomPermission.MailRead));

        // Assert
        Assert.False(refusal.RequiredPermission.IsSpecified);
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
        Action[] requirements =
        [
            () => authorization.RequirePermission(MailFathomPermission.MailRead),
            authorization.RequireProcessIdentity,
            authorization.RequireSignedCapability,
        ];

        // Act
        Exception?[] refusals = [.. requirements.Select(Record.Exception)];

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

    /// <summary>The boundary composing an answer per caller must agree with the one refusing an operation, or a listing would offer a tool the use case then refuses.</summary>
    [Fact]
    public void Permits_TheGrantAndTheRefusal_AgreeOnEveryPublishedPermission()
    {
        // Arrange
        var authorization = AuthorizationOver(
            AuthorizedPrincipal.Caller(ConfiguredCredentialName, [MailFathomPermission.MailRead]));

        // Act
        var reported = MailFathomPermission.All.Select(authorization.Permits).ToArray();
        var permitted = MailFathomPermission.All
            .Select(permission => Record.Exception(() => authorization.RequirePermission(permission)) is null)
            .ToArray();

        // Assert
        Assert.Equal(permitted, reported);
    }

    /// <summary>Neither of the two kinds a permission is never granted to may be reported as holding one.</summary>
    [Theory]
    [MemberData(nameof(EveryPublishedPermission))]
    public void Permits_APrincipalThatIsNotACaller_ReportsNothingHeld(MailFathomPermission permission)
    {
        // Arrange
        var processIdentity = AuthorizationOver(AuthorizedPrincipal.Process);
        var signedCapability = AuthorizationOver(AuthorizedPrincipal.SignedCapability("/attachments/an-object/0"));

        // Act, Assert
        Assert.False(processIdentity.Permits(permission));
        Assert.False(signedCapability.Permits(permission));
    }

    /// <summary>An entrypoint that stated nothing is reported as holding nothing rather than as a question nobody answered.</summary>
    [Fact]
    public void Permits_ReachedUnderNoPrincipal_ReportsNothingHeld()
    {
        // Arrange
        var authorization = AuthorizationOver(principal: null);

        // Act, Assert
        Assert.All(MailFathomPermission.All, permission => Assert.False(authorization.Permits(permission)));
    }

    /// <summary>A boundary asking about a capability nobody declared has found an operation nobody bounded, and the safe answer to that is no.</summary>
    [Fact]
    public void Permits_TheUnspecifiedPermission_ReportsNothingHeld()
    {
        // Arrange
        var authorization = AuthorizationOver(
            AuthorizedPrincipal.Caller(ConfiguredCredentialName, MailFathomPermission.All));

        // Act, Assert
        Assert.False(authorization.Permits(default));
    }

    /// <summary>A boundary recording a refusal has to name the credential, since the refusal itself may name nothing.</summary>
    [Fact]
    public void PrincipalIdentity_WorkAdmittedUnderACaller_ReportsWhatTheTransportAdmittedItAs()
    {
        // Arrange
        var authorization = AuthorizationOver(
            AuthorizedPrincipal.Caller(ConfiguredCredentialName, [MailFathomPermission.MailRead]));

        // Act, Assert
        Assert.Equal(ConfiguredCredentialName, authorization.PrincipalIdentity);
    }

    /// <summary>An entrypoint that stated nothing has nothing to name, and reporting a name for it would invent one.</summary>
    [Fact]
    public void PrincipalIdentity_WorkReachedUnderNoPrincipal_ReportsNothing()
    {
        // Arrange
        var authorization = AuthorizationOver(principal: null);

        // Act, Assert
        Assert.Null(authorization.PrincipalIdentity);
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

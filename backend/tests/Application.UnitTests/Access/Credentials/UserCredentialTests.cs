// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;
using Xunit;

namespace MailFathom.Application.UnitTests.Access.Credentials;

public sealed class UserCredentialTests
{
    private static readonly UserId User = UserId.Create(new Guid("0197c0de-0000-7000-8000-00000000ffff"));

    private static readonly DateTimeOffset ProvisionedAt = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A credential naming nothing holds exactly what its user's roles grant: a mail name no role carries — which is
    /// what a permission a later release publishes is until somebody writes it into one — is not held through it.
    /// </summary>
    [Fact]
    public void HeldUnder_ACredentialNamingNothing_HoldsExactlyTheMailHalfOfWhatItsUserHolds()
    {
        // Arrange
        var credential = CredentialNarrowedTo(permissions: null);
        var userGrant = GrantOf(MailFathomPermission.MailRead, MailFathomPermission.MailAsk, MailFathomPermission.AdminRead);

        // Act
        var held = credential.HeldUnder(userGrant);

        // Assert
        Assert.Equal([MailFathomPermission.MailRead, MailFathomPermission.MailAsk], held);
    }

    /// <summary>A listed name the user does not hold yields nothing, because the narrowing can only take away.</summary>
    [Fact]
    public void HeldUnder_ACredentialNamingSome_HoldsOnlyWhatBothItAndItsUserName()
    {
        // Arrange
        var credential = CredentialNarrowedTo([MailFathomPermission.MailRead, MailFathomPermission.MailSend]);
        var userGrant = GrantOf(MailFathomPermission.MailRead, MailFathomPermission.MailAsk);

        // Act
        var held = credential.HeldUnder(userGrant);

        // Assert
        Assert.Equal([MailFathomPermission.MailRead], held);
    }

    [Fact]
    public void HeldUnder_ACredentialNamingTheEmptyList_HoldsNothingWhateverItsUserHolds()
    {
        // Arrange
        var credential = CredentialNarrowedTo([]);
        var userGrant = ScopedGrant.AtDeployment(MailFathomPermission.PublishedFor(ProtectedSurface.Mail));

        // Act
        var held = credential.HeldUnder(userGrant);

        // Assert
        Assert.Empty(held);
    }

    private static ScopedGrant GrantOf(params MailFathomPermission[] permissions) =>
        ScopedGrant.Of(permissions.Select(permission => (permission, AssignmentScope.User(User))));

    private static UserCredential CredentialNarrowedTo(IReadOnlyList<MailFathomPermission>? permissions) => new(
        new Guid("0197c0de-0000-7000-8000-000000000001"),
        User,
        UserCredentialMethod.ApiKey,
        UserCredentialLookup.ForDigest("digest"),
        permissions,
        Enabled: true,
        Version: 1,
        ProvisionedAt,
        ProvisionedAt);
}

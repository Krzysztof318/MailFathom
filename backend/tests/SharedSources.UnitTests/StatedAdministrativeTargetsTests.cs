// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the targets every scope test arranges where an account or a user sits.</summary>
/// <remarks>
/// A fault here reports somebody else's arrangement. A double placing an unstated target anywhere a narrower scope
/// covers would let an out-of-scope test pass on an account the deployment does not hold — so the test would prove
/// nothing about the scope it names.
/// </remarks>
public sealed class StatedAdministrativeTargetsTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("0198f0aa-0000-7000-8000-0000000000f1");

    private static readonly UserId Owner = UserId.Create(new Guid("0198f0aa-0000-7000-8000-0000000000f2"));

    private static readonly Guid Organization = new("0198f0aa-0000-7000-8000-0000000000f3");

    [Fact]
    public async Task PlaceMailAccountsAsync_AStatedAccount_IsCoveredByItsOrganizationAndItsSoleAssignee()
    {
        // Arrange
        var targets = new StatedAdministrativeTargets().WithMailAccount(Account, Organization, Owner);

        // Act
        var placed = await targets.PlaceMailAccountsAsync([Account], TestContext.Current.CancellationToken);

        // Assert
        var target = Assert.Single(placed).Value;
        Assert.True(target.IsCoveredBy(AssignmentScope.Organization(Organization)));
        Assert.True(target.IsCoveredBy(AssignmentScope.User(Owner)));
    }

    [Fact]
    public async Task PlaceUserAsync_AStatedUser_IsCoveredByTheirOrganization()
    {
        // Arrange
        var targets = new StatedAdministrativeTargets().WithUser(Owner, Organization);

        // Act
        var target = await targets.PlaceUserAsync(Owner, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(target.IsCoveredBy(AssignmentScope.Organization(Organization)));
    }

    [Fact]
    public async Task PlaceAsync_NothingStated_PlacesEitherTargetWhereOnlyTheDeploymentCoversIt()
    {
        // Arrange
        var targets = new StatedAdministrativeTargets();

        // Act
        var account = Assert.Single(await targets.PlaceMailAccountsAsync([Account], TestContext.Current.CancellationToken)).Value;
        var user = await targets.PlaceUserAsync(Owner, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AdministrativeTarget.Unplaced, account);
        Assert.Equal(AdministrativeTarget.Unplaced, user);
        Assert.False(account.IsCoveredBy(AssignmentScope.Organization(Organization)));
        Assert.False(user.IsCoveredBy(AssignmentScope.User(Owner)));
        Assert.True(account.IsCoveredBy(AssignmentScope.Deployment));
    }
}

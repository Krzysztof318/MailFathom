// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Builds the authorization of an administrator holding one permission over one organization, for a test about what that scope admits.</summary>
internal static class OrganizationAdministrators
{
    /// <summary>Gets the organization the administrator holds the permission over.</summary>
    internal static Guid AdministeredOrganization { get; } = new("0198f0aa-0000-7000-8000-0000000000f1");

    /// <summary>Gets an organization the administrator holds nothing over.</summary>
    internal static Guid OtherOrganization { get; } = new("0198f0aa-0000-7000-8000-0000000000f2");

    /// <summary>Builds the authorization, reading where each named target sits from what the test stated.</summary>
    /// <param name="permission">The permission held over <see cref="AdministeredOrganization" />.</param>
    /// <param name="targets">Where each mail account or user the test names sits.</param>
    /// <returns>The authorization a use case reached by that administrator consults.</returns>
    internal static AccessAuthorization Holding(MailFathomPermission permission, StatedAdministrativeTargets targets) =>
        AccessAuthorizations.ForAdministratorScoped(
            ScopedGrant.Of([(permission, AssignmentScope.Organization(AdministeredOrganization))]),
            targets);

    /// <summary>Builds the authorization for a test naming one mail account, which sits in the organization it states.</summary>
    /// <param name="permission">The permission held over <see cref="AdministeredOrganization" />.</param>
    /// <param name="account">The account the test names.</param>
    /// <param name="accountOrganization">The organization the account sits in.</param>
    /// <returns>The authorization a use case reached by that administrator consults.</returns>
    internal static AccessAuthorization Holding(
        MailFathomPermission permission,
        MailAccountId account,
        Guid accountOrganization) =>
        Holding(permission, new StatedAdministrativeTargets().WithMailAccount(account, accountOrganization));
}

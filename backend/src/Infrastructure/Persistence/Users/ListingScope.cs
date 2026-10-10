// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Narrows an administrative listing's statement to what some scopes cover.</summary>
/// <remarks>
/// <para>
/// The scopes are turned into identifier arrays before the statement is composed, because a domain value's members
/// inside an <see cref="IQueryable{T}" /> lambda would not translate. Each array becomes one <c>= ANY</c> parameter, so
/// the statement's shape is the same however many scopes a caller holds.
/// </para>
/// <para>
/// The deployment scope covers everything and adds no predicate at all. An organization scope covers the organization
/// and its members; a user scope covers that user and no organization. No scope covers nothing, which is what a caller
/// holding a permission only elsewhere lists.
/// </para>
/// </remarks>
internal static class ListingScope
{
    /// <summary>Keeps the users some scopes cover.</summary>
    /// <param name="users">The users to select from.</param>
    /// <param name="within">The scopes the listing answers within.</param>
    /// <returns>The covered users.</returns>
    internal static IQueryable<UserAccountEntity> Covering(
        IQueryable<UserAccountEntity> users,
        IReadOnlySet<AssignmentScope> within)
    {
        if (within.Contains(AssignmentScope.Deployment))
        {
            return users;
        }

        var organizations = TargetsOf(within, AssignmentScopeKind.Organization);
        var named = TargetsOf(within, AssignmentScopeKind.User);

        return users.Where(user =>
            (user.OrganizationId != null && organizations.Contains(user.OrganizationId.Value)) || named.Contains(user.Id));
    }

    /// <summary>Keeps the organizations some scopes cover.</summary>
    /// <param name="organizations">The organizations to select from.</param>
    /// <param name="within">The scopes the listing answers within.</param>
    /// <returns>The covered organizations.</returns>
    internal static IQueryable<OrganizationEntity> Covering(
        IQueryable<OrganizationEntity> organizations,
        IReadOnlySet<AssignmentScope> within)
    {
        if (within.Contains(AssignmentScope.Deployment))
        {
            return organizations;
        }

        var covered = TargetsOf(within, AssignmentScopeKind.Organization);

        return organizations.Where(organization => covered.Contains(organization.Id));
    }

    private static Guid[] TargetsOf(IReadOnlySet<AssignmentScope> within, AssignmentScopeKind kind) =>
        [.. within.Where(scope => scope.Kind == kind).Select(static scope => scope.Target)];
}

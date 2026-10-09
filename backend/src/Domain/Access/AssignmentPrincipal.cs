// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Access;

/// <summary>Who a role assignment is given to: one user, or every member of one group.</summary>
public sealed record AssignmentPrincipal
{
    private AssignmentPrincipal(AssignmentPrincipalKind kind, Guid id)
    {
        this.Kind = kind;
        this.Id = id;
    }

    /// <summary>Gets whether the principal is a user or a group.</summary>
    public AssignmentPrincipalKind Kind { get; }

    /// <summary>Gets the identifier of the user or the group.</summary>
    public Guid Id { get; }

    /// <summary>Creates the principal naming one user.</summary>
    /// <param name="user">The user.</param>
    /// <returns>The principal.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    public static AssignmentPrincipal User(UserId user)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A user principal names a user.", nameof(user));
        }

        return new AssignmentPrincipal(AssignmentPrincipalKind.User, user.Value);
    }

    /// <summary>Creates the principal naming one group.</summary>
    /// <param name="groupId">The group.</param>
    /// <returns>The principal.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="groupId" /> is empty.</exception>
    public static AssignmentPrincipal Group(Guid groupId)
    {
        if (groupId == Guid.Empty)
        {
            throw new ArgumentException("A group principal names a group.", nameof(groupId));
        }

        return new AssignmentPrincipal(AssignmentPrincipalKind.Group, groupId);
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Access;

/// <summary>Where one thing an administrative operation names sits in the deployment: the organization it belongs to, and the one user it is wholly theirs.</summary>
/// <remarks>
/// <para>
/// A scope covers a target by one of those two facts or by being the deployment, and by nothing else: an organization
/// covers what belongs to it, and a user scope covers what is that user's alone. A user is wholly their own; a mail
/// account is one user's only while it is assigned to nobody else, because a mailbox assigned to two people is read by
/// both and what reaches mail another person reads is not one user's to do.
/// </para>
/// <para>
/// A mail account in no organization is covered by the deployment scope alone, whoever it is assigned to, while a user
/// in none is covered by the deployment and by a scope naming them. Something in no organization and nobody's alone is
/// what a target the deployment does not hold resolves to — so a target nobody can place is reached by nothing narrower
/// than the deployment, and a lookup that found nothing never widens what a scope covers.
/// </para>
/// </remarks>
public sealed record AdministrativeTarget
{
    private AdministrativeTarget(Guid? organization, UserId? soleUser)
    {
        this.Organization = organization;
        this.SoleUser = soleUser;
    }

    /// <summary>Gets the target in no organization and nobody's alone, which the deployment scope alone covers.</summary>
    public static AdministrativeTarget Unplaced { get; } = new(organization: null, soleUser: null);

    /// <summary>Gets the organization the target belongs to, or <see langword="null" /> for none.</summary>
    public Guid? Organization { get; }

    /// <summary>Gets the one user the target is wholly the concern of, or <see langword="null" /> where it is nobody's alone.</summary>
    public UserId? SoleUser { get; }

    /// <summary>Places a user.</summary>
    /// <param name="user">The user.</param>
    /// <param name="organization">The organization the user is a member of, or <see langword="null" /> for none.</param>
    /// <returns>The target.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    public static AdministrativeTarget User(UserId user, Guid? organization)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A user target names a user.", nameof(user));
        }

        return new AdministrativeTarget(NamedOrNone(organization), user);
    }

    /// <summary>Places a mail account.</summary>
    /// <param name="organization">The organization the account belongs to, or <see langword="null" /> for none.</param>
    /// <param name="assignedUsers">Every user the account is assigned to.</param>
    /// <returns>The target, which is one user's alone only where it belongs to an organization and exactly one user is assigned.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assignedUsers" /> is <see langword="null" />.</exception>
    public static AdministrativeTarget MailAccount(Guid? organization, IReadOnlyCollection<UserId> assignedUsers)
    {
        ArgumentNullException.ThrowIfNull(assignedUsers);

        var placed = NamedOrNone(organization);
        var distinctUsers = assignedUsers.Where(user => user.IsSpecified).Distinct().ToArray();

        return new AdministrativeTarget(
            placed,
            placed is not null && distinctUsers is [var soleUser] ? soleUser : null);
    }

    /// <summary>Reports whether one scope covers this target.</summary>
    /// <param name="scope">The scope an assignment names.</param>
    /// <returns><see langword="true" /> for the deployment, for the organization the target belongs to, and for the user it is wholly the concern of.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    public bool IsCoveredBy(AssignmentScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return scope.Kind switch
        {
            AssignmentScopeKind.Deployment => true,
            AssignmentScopeKind.Organization => this.Organization == scope.Target,
            AssignmentScopeKind.User => this.SoleUser?.Value == scope.Target,
            _ => false,
        };
    }

    private static Guid? NamedOrNone(Guid? organization) => organization == Guid.Empty ? null : organization;
}

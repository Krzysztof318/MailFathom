// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Access;

/// <summary>What a role assignment reaches: the whole deployment, one organization, or one user.</summary>
/// <remarks>
/// A reference type rather than a struct, so that no default value can stand for a scope: the struct default would
/// read as the deployment, which is the widest grant there is, and a value nobody chose must never be that one.
/// </remarks>
public sealed record AssignmentScope
{
    private AssignmentScope(AssignmentScopeKind kind, Guid target)
    {
        this.Kind = kind;
        this.Target = target;
    }

    /// <summary>Gets the scope covering everything the deployment holds.</summary>
    public static AssignmentScope Deployment { get; } = new(AssignmentScopeKind.Deployment, Guid.Empty);

    /// <summary>Gets which of the three scopes this is.</summary>
    public AssignmentScopeKind Kind { get; }

    /// <summary>Gets the organization or the user the scope names, or <see cref="Guid.Empty" /> for the deployment.</summary>
    public Guid Target { get; }

    /// <summary>Creates the scope covering one organization.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <returns>The scope.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="organizationId" /> is empty.</exception>
    public static AssignmentScope Organization(Guid organizationId)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("An organization scope names an organization.", nameof(organizationId));
        }

        return new AssignmentScope(AssignmentScopeKind.Organization, organizationId);
    }

    /// <summary>Creates the scope covering one user.</summary>
    /// <param name="user">The user.</param>
    /// <returns>The scope.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    public static AssignmentScope User(UserId user)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A user scope names a user.", nameof(user));
        }

        return new AssignmentScope(AssignmentScopeKind.User, user.Value);
    }
}

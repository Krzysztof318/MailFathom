// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One role given to one user or one group, at the deployment, one organization, or one user.</summary>
/// <remarks>
/// The principal and the scope are each a pair of nullable foreign keys rather than a kind beside an untyped
/// identifier, so that the database itself refuses an assignment naming something that does not exist, and removing a
/// user, a group, or an organization takes every assignment naming it with it. Exactly one principal column is set, and
/// at most one scope column — neither meaning the deployment — which a check constraint holds.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class RoleAssignmentEntity
{
    /// <summary>The table these rows live in.</summary>
    internal const string TableName = "role_assignments";

    /// <summary>The identifier the deployment generated, which a revocation names.</summary>
    public Guid Id { get; set; }

    /// <summary>The role given.</summary>
    public Guid RoleId { get; set; }

    /// <summary>The user it is given to, when the principal is a user.</summary>
    public Guid? PrincipalUserId { get; set; }

    /// <summary>The group it is given to, when the principal is a group.</summary>
    public Guid? PrincipalGroupId { get; set; }

    /// <summary>The organization it reaches, when the scope is one organization.</summary>
    public Guid? ScopeOrganizationId { get; set; }

    /// <summary>The user it reaches, when the scope is one user.</summary>
    public Guid? ScopeUserId { get; set; }

    /// <summary>When the role was given.</summary>
    public DateTimeOffset AssignedAt { get; set; }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Grants;

/// <summary>Records that an administrator changed a role, a group, or a role assignment.</summary>
/// <remarks>
/// <para>
/// Each of these changes what somebody may do — a role's list reaches everybody assigned it, a membership is receiving
/// the group's assignments, and an assignment is a grant — and none of them leaves a trace anywhere else. The record is
/// what an operator reads when a permission turns up that nobody remembers giving, beside the explanation
/// <see cref="GrantAdministration.ExplainUserGrantAsync" /> gives of where it comes from now.
/// </para>
/// <para>
/// The port is narrow for the reason <see cref="Credentials.IUserCredentialAuditor" /> is: the sink behind it is
/// undecided — structured logging today, an audit table or an external evidence store once compliance evidence is
/// collected — and one operation is all a caller may reach for.
/// </para>
/// </remarks>
public interface IGrantAuditor
{
    /// <summary>Records one change to roles, groups, or assignments.</summary>
    /// <param name="change">The act, the record it changed, and the administrator who asked.</param>
    /// <param name="cancellationToken">Cancels writing the record.</param>
    /// <returns>A task that completes once the record is durable for the configured sink.</returns>
    Task RecordGrantChangeAsync(GrantChange change, CancellationToken cancellationToken);
}

/// <summary>One administrative change to a role, a group, or a role assignment.</summary>
/// <param name="Act">What was done.</param>
/// <param name="RecordId">The role, the group, or the assignment it was done to.</param>
/// <param name="ActingAdministrator">What the administrative surface admitted the caller as.</param>
/// <param name="OccurredAt">When the change committed.</param>
/// <remarks>
/// A record names identifiers and published permission names and nothing an operator wrote as a label: a role's or a
/// group's name is read from its listing by whoever may read one, and a renamed record is reached by the identifier that
/// stayed the same.
/// </remarks>
public sealed record GrantChange(GrantAct Act, Guid RecordId, string ActingAdministrator, DateTimeOffset OccurredAt)
{
    /// <summary>Gets the user who joined or left a group, for <see cref="GrantAct.GroupMemberAdded" /> and <see cref="GrantAct.GroupMemberRemoved" />.</summary>
    public UserId? Member { get; init; }

    /// <summary>Gets what was given or taken away, for <see cref="GrantAct.RoleAssigned" /> and <see cref="GrantAct.AssignmentRevoked" />.</summary>
    /// <remarks>Carried whole because a revoked assignment can no longer be read back by its identifier.</remarks>
    public RoleAssignment? Assignment { get; init; }

    /// <summary>Gets what a role grants from now on, for <see cref="GrantAct.RoleCreated" /> and <see cref="GrantAct.RolePermissionsReplaced" />: each entry as it was written, a pattern as the pattern.</summary>
    /// <remarks>The entry rather than what it reaches, because what a pattern reaches is the build's to decide after the record is written, and the record is of what somebody wrote.</remarks>
    public IReadOnlyList<string>? Permissions { get; init; }
}

/// <summary>Which administrative act a grant record describes.</summary>
public enum GrantAct
{
    /// <summary>A role was recorded.</summary>
    RoleCreated = 0,

    /// <summary>A role's name was replaced.</summary>
    RoleRenamed = 1,

    /// <summary>The whole list of permissions a role grants was replaced, which reached everybody assigned it.</summary>
    RolePermissionsReplaced = 2,

    /// <summary>A role nobody was assigned was removed.</summary>
    RoleDeleted = 3,

    /// <summary>A group was recorded.</summary>
    GroupCreated = 4,

    /// <summary>A group's name was replaced.</summary>
    GroupRenamed = 5,

    /// <summary>A group nothing was assigned to was removed, with its memberships.</summary>
    GroupDeleted = 6,

    /// <summary>A user joined a group, receiving every assignment it holds.</summary>
    GroupMemberAdded = 7,

    /// <summary>A user left a group, losing every assignment it holds.</summary>
    GroupMemberRemoved = 8,

    /// <summary>A role was given to a user or a group at a scope.</summary>
    RoleAssigned = 9,

    /// <summary>A role assignment was revoked.</summary>
    AssignmentRevoked = 10,
}

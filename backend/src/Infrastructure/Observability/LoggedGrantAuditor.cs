// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Grants;
using MailFathom.Domain.Access;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Writes changes to roles, groups, and role assignments to the structured log as the deployment's audit record.</summary>
/// <remarks>
/// <para>
/// One line per act, carrying the identifiers an operator follows from a surprising permission back to the act that gave
/// it: the role, the group, the user, the assignment's principal and scope, and the administrator the request was
/// admitted as. A role's list is written as the published names, which are this repository's vocabulary rather than
/// anything about a person. A durable audit store replaces this implementation without any caller changing.
/// </para>
/// <para>
/// No line carries a role's or a group's name: those are labels an operator wrote, and the identifier is what stays the
/// same across a rename.
/// </para>
/// </remarks>
internal sealed partial class LoggedGrantAuditor(ILogger<LoggedGrantAuditor> logger) : IGrantAuditor
{
    /// <inheritdoc />
    public Task RecordGrantChangeAsync(GrantChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (change.Assignment is { } assignment)
        {
            this.LogAssignmentChanged(
                change.Act,
                assignment.Id,
                assignment.RoleId,
                assignment.Principal.Kind,
                assignment.Principal.Id,
                assignment.Scope.Kind,
                assignment.Scope.Target,
                change.ActingAdministrator,
                change.OccurredAt);
        }
        else if (change.Member is { } member)
        {
            this.LogMembershipChanged(change.Act, member.Value, change.RecordId, change.ActingAdministrator, change.OccurredAt);
        }
        else
        {
            this.LogRecordChanged(
                change.Act,
                change.RecordId,
                change.Permissions is { } permissions
                    ? string.Join(' ', permissions)
                    : "-",
                change.ActingAdministrator,
                change.OccurredAt);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Role or group {RecordId} was {GrantAct} by {ActingAdministrator} at {OccurredAt}; the permissions it "
            + "grants from now on, where the act set them: {Permissions}.")]
    private partial void LogRecordChanged(
        GrantAct grantAct,
        Guid recordId,
        string permissions,
        string actingAdministrator,
        DateTimeOffset occurredAt);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "User {UserId} was the subject of {GrantAct} on group {GroupId} by {ActingAdministrator} at "
            + "{OccurredAt}, which gave or took every assignment the group holds.")]
    private partial void LogMembershipChanged(
        GrantAct grantAct,
        Guid userId,
        Guid groupId,
        string actingAdministrator,
        DateTimeOffset occurredAt);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Role assignment {AssignmentId} was {GrantAct} by {ActingAdministrator} at {OccurredAt}: role {RoleId} "
            + "to {PrincipalKind} {PrincipalId} at the {ScopeKind} scope {ScopeTarget}.")]
    private partial void LogAssignmentChanged(
        GrantAct grantAct,
        Guid assignmentId,
        Guid roleId,
        AssignmentPrincipalKind principalKind,
        Guid principalId,
        AssignmentScopeKind scopeKind,
        Guid scopeTarget,
        string actingAdministrator,
        DateTimeOffset occurredAt);
}

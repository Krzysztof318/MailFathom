// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access.Grants;

/// <summary>What a write to a role, a group, or an assignment did, or why it did nothing.</summary>
public enum GrantWriteOutcome
{
    /// <summary>The write was performed, or what it asked for already held.</summary>
    Written = 0,

    /// <summary>Another role, or another group, already carries the name.</summary>
    NameTaken = 1,

    /// <summary>No role carries the identifier the write named.</summary>
    UnknownRole = 2,

    /// <summary>No group carries the identifier the write named.</summary>
    UnknownGroup = 3,

    /// <summary>No user carries the identifier the write named, as a member, a principal, or a scope.</summary>
    UnknownUser = 4,

    /// <summary>No organization carries the identifier the write named, as a group's organization or a scope.</summary>
    UnknownOrganization = 5,

    /// <summary>No assignment carries the identifier the write named.</summary>
    UnknownAssignment = 6,

    /// <summary>The same role is already given to the same principal at the same scope.</summary>
    AlreadyAssigned = 7,

    /// <summary>The role or the group is still assigned, so deleting it was refused rather than revoking every grant it carries.</summary>
    StillAssigned = 8,

    /// <summary>The user belongs to a different organization from the group, which holds only its own organization's members.</summary>
    OutsideGroupOrganization = 9,
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Access;

/// <summary>Whether a role assignment is given to a user or to a group.</summary>
public enum AssignmentPrincipalKind
{
    /// <summary>One user.</summary>
    User = 0,

    /// <summary>One group, whose members each hold what it is assigned.</summary>
    Group = 1,
}

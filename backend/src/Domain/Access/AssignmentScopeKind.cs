// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Access;

/// <summary>Which of the three scopes a role assignment reaches.</summary>
public enum AssignmentScopeKind
{
    /// <summary>Everything the deployment holds.</summary>
    Deployment = 0,

    /// <summary>One organization: its members, its mail accounts, its groups, and the assignments inside it.</summary>
    Organization = 1,

    /// <summary>One user: their own record, credentials, contact book, and the assignments made at that scope.</summary>
    User = 2,
}

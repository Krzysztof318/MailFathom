// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Tasks;

/// <summary>What became of a request to move one task's state.</summary>
/// <remarks>
/// There are two answers rather than three, because a state a task already stands in is the state the caller asked
/// for: accepting an accepted proposal and completing a completed task each leave the list exactly as the caller
/// wanted it, and reporting either as a failure would make a repeated press an error rather than a no-op.
/// </remarks>
public enum PersonalTaskChangeOutcome
{
    /// <summary>The task stands in the state that was asked for.</summary>
    Applied = 0,

    /// <summary>This user holds no such task, which is also the answer for one another user holds.</summary>
    NotFound = 1,
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access.Grants;

/// <summary>What a write to a role, a group, or an assignment did, and what a refusal has to name for an administrator to act on it.</summary>
/// <param name="Outcome">What the write did, or why it did nothing.</param>
/// <param name="StandingAssignments">How many assignments stood in the way of a deletion; zero for every other outcome.</param>
/// <param name="RecordId">The identifier a role, a group, or an assignment was recorded under, carried by a creation that was written and empty otherwise.</param>
public sealed record GrantWriteResult(GrantWriteOutcome Outcome, int StandingAssignments = 0, Guid RecordId = default)
{
    /// <summary>Gets the result of a write that ended in the outcome given.</summary>
    /// <param name="outcome">What the write did.</param>
    /// <returns>The result.</returns>
    public static GrantWriteResult Of(GrantWriteOutcome outcome) => new(outcome);
}

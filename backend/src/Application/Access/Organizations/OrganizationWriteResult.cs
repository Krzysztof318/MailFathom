// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access.Organizations;

/// <summary>What an act on an organization did, and what a refusal has to name for an administrator to act on it.</summary>
/// <param name="Outcome">What the act did, or why it did nothing.</param>
/// <param name="OrganizationId">The organization the act reached, which a creation reports because it minted it.</param>
/// <param name="RemainingMembers">How many members stood in the way of a deletion; zero for every other outcome.</param>
/// <param name="CollidingUsername">The username the target scope already holds, where a move was refused for one and the collision could be read.</param>
public sealed record OrganizationWriteResult(
    OrganizationWriteOutcome Outcome,
    Guid OrganizationId = default,
    int RemainingMembers = 0,
    string? CollidingUsername = null)
{
    /// <summary>Gets the result of an act that changed nothing because of the outcome given.</summary>
    /// <param name="outcome">What the act did.</param>
    /// <returns>The result.</returns>
    public static OrganizationWriteResult Of(OrganizationWriteOutcome outcome) => new(outcome);
}

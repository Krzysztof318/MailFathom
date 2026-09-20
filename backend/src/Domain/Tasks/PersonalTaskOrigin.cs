// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Tasks;

/// <summary>Where a task came from, which is what decides whether the person has committed to it.</summary>
/// <remarks>
/// It is the split a calendar event carries for the same reason: MailFathom may read a commitment out of mail, and
/// what it read is a suggestion until the person says otherwise. Accepting a proposal moves the task along this axis
/// rather than writing a second one, so a task keeps one identity from the moment it was derived.
/// </remarks>
public enum PersonalTaskOrigin
{
    /// <summary>The person entered it, or accepted a proposal, and owes it.</summary>
    Asserted = 0,

    /// <summary>MailFathom derived it and is offering it, and nobody has committed to it yet.</summary>
    Proposed = 1,
}

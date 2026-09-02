// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Contacts;

/// <summary>States how a contact came to be in the book, and thereby which writer may amend it.</summary>
/// <remarks>
/// <para>
/// The two values are different claims about the same kind of thing. An asserted contact is one somebody wrote down; a
/// collected one is an address that merely appeared in mail that arrived. Both live in one book, because searching for a
/// person should not require knowing which half they are in, and the difference is kept because it decides what may
/// happen to the record without anybody asking: collection writes only into its own origin, and an asserted contact is
/// never touched by it.
/// </para>
/// <para>
/// The value therefore doubles as the authority a write carries. A writer amends the contacts of its own origin and no
/// others, which is what makes the rule structural rather than a convention each writer has to remember.
/// </para>
/// </remarks>
public enum ContactOrigin
{
    /// <summary>Somebody wrote this person down.</summary>
    Asserted = 0,

    /// <summary>The address appeared in mail that arrived, and collection recorded it.</summary>
    Collected = 1,
}

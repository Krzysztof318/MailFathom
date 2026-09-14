// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Records;

/// <summary>Which kind of stored record a roster reading refused, which decides what the refusal costs.</summary>
/// <remarks>
/// The two are not degrees of the same thing: a user this deployment cannot read is a person it serves nothing for,
/// and one of their mail accounts is a mailbox it does not synchronize while the rest of their mail keeps working. An
/// operator reads the kind first because it says which of their people noticed. An organization is not among them —
/// no roster binds one, so an unreadable organization row is answered from the rows themselves as an
/// <c>UnreadableOrganization</c> rather than held here.
/// </remarks>
internal enum HeldBackRecordKind
{
    /// <summary>A user whose own record does not read as the settings a user's document holds.</summary>
    User = 0,

    /// <summary>One mail account declaration of a user whose record is otherwise readable.</summary>
    MailAccount = 1,
}

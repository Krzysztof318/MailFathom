// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Records;

/// <summary>Which kind of stored record a deployment refused, which decides what the refusal costs.</summary>
/// <remarks>
/// The three are not degrees of the same thing. A user this deployment cannot read is a person it serves nothing for;
/// one of their mail accounts is a mailbox it does not synchronize while the rest of their mail keeps working; an
/// organization is the scope a login is typed in. An operator reads the kind first because it says which of their
/// people noticed.
/// </remarks>
internal enum HeldBackRecordKind
{
    /// <summary>A user whose own record does not read as the settings a user's document holds.</summary>
    User = 0,

    /// <summary>One mail account declaration of a user whose record is otherwise readable.</summary>
    MailAccount = 1,

    /// <summary>An organization whose stored row does not read as one this deployment accepts.</summary>
    Organization = 2,
}

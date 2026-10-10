// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Policies;

/// <summary>What a settings policy resolves a governable property as.</summary>
internal enum GovernablePropertyShape
{
    /// <summary>One value: a leaf of the record.</summary>
    Value = 0,

    /// <summary>One value that is a list, which a policy states whole and never entry by entry.</summary>
    List = 1,

    /// <summary>A block of properties, each resolved on its own; a path naming it covers every one beneath it.</summary>
    Block = 2,

    /// <summary>A secret block, named as a whole, about whose value a policy states nothing.</summary>
    Secret = 3,
}

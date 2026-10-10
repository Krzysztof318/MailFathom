// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Policies;

/// <summary>What the list of an editing restriction means, which a policy picks for each of its sections.</summary>
/// <remarks>The names say what the list holds, which <em>open</em> and <em>closed</em> would have left to memory.</remarks>
internal enum SettingsPolicyEditingMode
{
    /// <summary>Everything is the person's to change except the properties listed, which is what a section stating no restriction means.</summary>
    AllExcept = 0,

    /// <summary>Nothing is the person's to change except the properties listed, so a property a later release adds starts out locked.</summary>
    NoneExcept = 1,
}

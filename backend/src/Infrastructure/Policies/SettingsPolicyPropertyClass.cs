// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Policies;

/// <summary>Which of a settings policy's three statements a property of a governed record takes.</summary>
/// <remarks>
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0037-a-settings-policy-at-the-deployment-and-at-each-organization.md">ADR 0037</see>
/// § <em>Three classes of property</em> holds why there are three and what each one refuses.
/// </remarks>
public enum SettingsPolicyPropertyClass
{
    /// <summary>A property that takes a default, a forced value, and the editing restriction, which is what one is unless it is marked otherwise.</summary>
    Ordinary = 0,

    /// <summary>A property saying who or which rather than how, which takes the editing restriction and neither a default nor a forced value.</summary>
    Identity = 1,

    /// <summary>A property only an administrator writes, which takes a default and a forced value and which no editing mode makes the person's to change.</summary>
    AdministratorOnly = 2,
}

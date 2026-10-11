// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Policies;

/// <summary>Which kind of governed record a policy write was judged against.</summary>
public enum SettingsPolicyRecordKind
{
    /// <summary>A user's record.</summary>
    User = 0,

    /// <summary>A mail account.</summary>
    MailAccount = 1,
}

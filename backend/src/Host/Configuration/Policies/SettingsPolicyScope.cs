// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Policies;

/// <summary>Where a settings policy is held, which is also the whole answer to who holds a setting.</summary>
internal enum SettingsPolicyScope
{
    /// <summary>The deployment's own policy, which governs every record.</summary>
    Deployment = 0,

    /// <summary>The policy of the organization a record belongs to.</summary>
    Organization = 1,
}

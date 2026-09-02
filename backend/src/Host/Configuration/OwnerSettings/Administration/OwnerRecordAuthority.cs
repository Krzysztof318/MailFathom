// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.OwnerSettings.Administration;

/// <summary>Who a change to an owner's record is being made by, which decides one rule and no other.</summary>
/// <remarks>
/// Every other rule a candidate is judged by is the same for both, which is the point of the two callers sharing one
/// service. What differs is the secret-bearing settings: an administrator acts for the deployment and names the
/// references it resolves, while an owner acts for themselves and may not name one at all — a reference is a path into
/// whatever this deployment can read, and the mailbox it would be presented to is the owner's own.
/// </remarks>
internal enum OwnerRecordAuthority
{
    /// <summary>The deployment's own administrator, acting for it rather than for a person.</summary>
    Administrator = 0,

    /// <summary>The owner themselves, acting on their own record through an owner-facing surface.</summary>
    Owner = 1,
}

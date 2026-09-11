// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>What erasing a user did, and whether this process was serving them.</summary>
/// <param name="UserErased">Whether a user record was there to remove, so a repeat is reported as the no-op it is.</param>
/// <param name="WasServed">Whether the runtime roster held the user that was erased.</param>
/// <remarks>
/// The second says whether removing the user also changed the running process. A served user leaves the runtime roster
/// after the erasure commits, so callers and synchronization stop reaching them without a restart.
/// </remarks>
internal readonly record struct UserErasureOutcome(bool UserErased, bool WasServed);

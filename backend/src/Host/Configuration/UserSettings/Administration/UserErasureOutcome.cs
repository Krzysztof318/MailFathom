// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>What erasing a user did, whether this process was serving them, and why nothing was erased.</summary>
/// <param name="UserErased">Whether a user record was there to remove, so a repeat is reported as the no-op it is.</param>
/// <param name="WasServed">Whether the runtime roster held the user that was erased.</param>
/// <param name="RefusalMessage">The sentence naming what has to change before this user can be erased, or <see langword="null" /> where the erasure ran.</param>
/// <remarks>
/// <para>
/// The second says whether removing the user also changed the running process. A served user leaves the runtime roster
/// after the erasure commits, so callers and synchronization stop reaching them without a restart.
/// </para>
/// <para>
/// The third is a refusal rather than an outcome, and it exists because one erasure undoes itself: a start writes an
/// user a configuration source names back into the roster, under the identifier the declaration carries and with the
/// mail accounts it supplies, so a deletion request answered against one of them would be answered and then reversed.
/// </para>
/// </remarks>
internal readonly record struct UserErasureOutcome(bool UserErased, bool WasServed, string? RefusalMessage = null);

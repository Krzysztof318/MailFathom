// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.OwnerSettings.Administration;

/// <summary>What this deployment records a person as, and whether it would accept them changing it.</summary>
/// <param name="DisplayName">The label the envelope carries, which is what a screen shows the person as.</param>
/// <param name="Changeable">Whether a write of it from this caller would be accepted.</param>
/// <remarks>
/// The second fact travels with the first because a person who may not write it must still see it: their mail accounts
/// are declared in the deployment's own files, or their credential was never granted the record's write, and neither is
/// a reason to draw an anonymous screen. Answering it here is what lets a client draw the name as text rather than
/// discovering the refusal by submitting a change of it.
/// </remarks>
internal readonly record struct OwnDisplayNameReading(string DisplayName, bool Changeable);

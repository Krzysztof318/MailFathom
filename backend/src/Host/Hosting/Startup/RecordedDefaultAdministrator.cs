// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Host.Hosting.Startup;

/// <summary>The default administrator this deployment holds, as its startup gate read it.</summary>
/// <remarks>
/// What an administrative endpoint that authenticates nobody serves its callers as. It is read once per start rather
/// than per request because the record is written once and only ever cleared: a default administrator removed while
/// this replica runs leaves a user nothing grants anything to, so every route refuses until the next start reads the
/// record as empty.
/// </remarks>
internal sealed class RecordedDefaultAdministrator
{
    /// <summary>Gets the default administrator, or <see langword="null" /> before the gate ran and where it was removed.</summary>
    internal UserId? User { get; private set; }

    /// <summary>Records what the startup gate read.</summary>
    /// <param name="user">The default administrator, or <see langword="null" /> where it was removed.</param>
    internal void Record(UserId? user) => this.User = user;
}

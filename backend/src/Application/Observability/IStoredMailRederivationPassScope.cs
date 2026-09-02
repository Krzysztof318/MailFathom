// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Maintenance;

namespace MailFathom.Application.Observability;

/// <summary>Holds one bounded pass's report open, and takes the counts the pass committed.</summary>
/// <remarks>
/// A pass that never reports what it committed was stopped part way through, and what its batches had already committed
/// stays committed. Only what a pass reported is counted, so the measurements never claim work that a cancelled pass
/// left half done.
/// </remarks>
public interface IStoredMailRederivationPassScope : IDisposable
{
    /// <summary>Records what the pass re-read, stepped over, and could not find content for.</summary>
    /// <param name="pass">What the pass committed.</param>
    void Completed(StoredMailRederivationPass pass);
}

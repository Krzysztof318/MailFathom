// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Observability;

/// <summary>Holds one folder-run stage's report open for as long as the stage is running.</summary>
/// <remarks>
/// The report is open <em>around</em> the stage rather than written after it, so the mail-session and database work the
/// stage causes is reported beneath it. A stage that never reports having completed ended in shutdown or in a failure,
/// and the two are told apart by the run's own token rather than by catching anything here.
/// </remarks>
public interface IMailSynchronizationPhaseScope : IDisposable
{
    /// <summary>Records that the stage ran to its end.</summary>
    /// <remarks>
    /// Called once, on the path that reached the end of the stage. A stage that did nothing because there was nothing
    /// to do still completed: what this separates is work that finished from work that was stopped.
    /// </remarks>
    void Completed();
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Synchronization;

/// <summary>States what makes a folder's next synchronization pass start.</summary>
/// <remarks>
/// <para>
/// The same two names describe what an operator asked for and what a folder actually got, because the second is only
/// ever the first or the fallback below it. A server that advertises no push mechanism, and a folder whose push
/// attempts kept failing, both leave the folder on <see cref="Polling" /> while configuration still says
/// <see cref="Push" /> — which is exactly why the effective mode is reported rather than inferred from configuration.
/// </para>
/// <para>
/// Neither mode changes what a pass does. Both run the same synchronization, over the same read-only session, with the
/// same bounds; the mode decides only when the next one begins.
/// </para>
/// </remarks>
public enum MailSynchronizationMode
{
    /// <summary>The folder is reconciled on the account's configured interval and nothing else starts a pass.</summary>
    Polling = 0,

    /// <summary>The folder holds a session that waits for the mail server to report a change, and a change starts a pass at once.</summary>
    Push = 1,
}

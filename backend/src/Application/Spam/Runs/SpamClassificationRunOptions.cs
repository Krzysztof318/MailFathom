// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Spam.Runs;

/// <summary>Bounds how much of an account's mail one classification pass takes in hand.</summary>
/// <remarks>
/// <para>
/// Neither setting is a schedule. Carrying a run is a step of the account's synchronization run, so how often a pass
/// happens is that run's interval and its backoff; what is configured here is how wide one pass is allowed to be, which
/// is what stops a mailbox nobody has scored from turning one run into a walk of its whole history while the folders the
/// run exists to fetch wait behind it.
/// </para>
/// <para>
/// Both defaults are smaller than the rule pass's, because the work per message is not comparable. A rule evaluates an
/// expression over state that is already in memory; a classification reads the stored message and, where a scanner is
/// configured, sends the whole of it across a socket and waits for a score.
/// </para>
/// </remarks>
public sealed class SpamClassificationRunOptions
{
    /// <summary>Gets or sets how many stored occurrences one batch reads, classifies, and commits the position of.</summary>
    /// <remarks>
    /// A batch is the unit of progress an interrupted pass gives back. Raising it walks a mailbox in fewer commits;
    /// lowering it shortens the stretch a cancelled pass has to cover again.
    /// </remarks>
    public int BatchSize { get; set; } = 50;

    /// <summary>Gets or sets how many batches one pass may commit before it leaves the rest to the next account run.</summary>
    public int MaxBatchesPerPass { get; set; } = 4;
}

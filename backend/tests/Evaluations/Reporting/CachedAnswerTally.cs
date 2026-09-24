// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Evaluations.Reporting;

/// <summary>How many answers a model under test gave were read from the response cache, and how many were asked of it afresh.</summary>
/// <remarks>
/// A run read from the cache replays the sample an earlier run drew, so its verdicts say nothing new about the model;
/// counting both halves is what lets a report tell a measurement from a replay.
/// </remarks>
internal sealed class CachedAnswerTally
{
    private int read;

    private int asked;

    /// <summary>Gets how many answers were read from the cache.</summary>
    public int Read => Volatile.Read(ref this.read);

    /// <summary>Gets how many answers were asked of the model.</summary>
    public int Asked => Volatile.Read(ref this.asked);

    /// <summary>Counts one answer.</summary>
    /// <param name="readFromCache">Whether the cache held the answer.</param>
    public void Count(bool readFromCache)
    {
        if (readFromCache)
        {
            Interlocked.Increment(ref this.read);
        }
        else
        {
            Interlocked.Increment(ref this.asked);
        }
    }
}

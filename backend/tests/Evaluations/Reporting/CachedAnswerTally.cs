// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Evaluations.Reporting;

/// <summary>How many answers a model under test gave were replayed from an earlier run, reused from this run's own, and asked of it afresh.</summary>
/// <remarks>
/// An answer replayed from an earlier run repeats the sample that run drew, so its verdict says nothing new about the
/// model; an answer this run wrote and read back is still this run's measurement, which a scenario asking the same
/// question under several settings relies on. Counting the three apart is what lets a report tell a measurement from a
/// replay.
/// </remarks>
internal sealed class CachedAnswerTally
{
    private int replayed;

    private int reused;

    private int asked;

    /// <summary>Gets how many answers were read from entries an earlier run wrote.</summary>
    public int Replayed => Volatile.Read(ref this.replayed);

    /// <summary>Gets how many answers were read from entries this run wrote.</summary>
    public int Reused => Volatile.Read(ref this.reused);

    /// <summary>Gets how many answers were asked of the model.</summary>
    public int Asked => Volatile.Read(ref this.asked);

    /// <summary>Counts one answer read from an entry an earlier run wrote.</summary>
    public void CountReplayed() => Interlocked.Increment(ref this.replayed);

    /// <summary>Counts one answer read from an entry this run wrote.</summary>
    public void CountReused() => Interlocked.Increment(ref this.reused);

    /// <summary>Counts one answer asked of the model.</summary>
    public void CountAsked() => Interlocked.Increment(ref this.asked);
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Evaluations.Costing;

/// <summary>What one client has spent since it was last read.</summary>
/// <remarks>
/// One meter belongs to one client rather than to the run, because several models are measured at the same time and the
/// judge grades each of them: a shared counter would report whichever run happened to finish beside it.
/// </remarks>
internal sealed class SpendMeter
{
    private readonly Lock counters = new();

    private int calls;
    private long inputTokens;
    private long outputTokens;
    private decimal? cost;

    /// <summary>Records one answered request.</summary>
    /// <param name="requestTokens">The tokens the request carried.</param>
    /// <param name="answerTokens">The tokens it was answered with.</param>
    /// <param name="charge">What the provider charged for it, where the answer carried a charge.</param>
    public void Record(long requestTokens, long answerTokens, decimal? charge)
    {
        lock (this.counters)
        {
            this.calls++;
            this.inputTokens += requestTokens;
            this.outputTokens += answerTokens;
            this.cost = charge is null ? this.cost : (this.cost ?? 0m) + charge;
        }
    }

    /// <summary>Takes what has been spent since the last reading, and starts counting again.</summary>
    /// <returns>The calls, tokens, and charge that reached the provider.</returns>
    public PaidUsage Take()
    {
        lock (this.counters)
        {
            var paid = new PaidUsage(this.calls, this.inputTokens, this.outputTokens, this.cost);

            this.calls = 0;
            this.inputTokens = 0;
            this.outputTokens = 0;
            this.cost = null;

            return paid;
        }
    }
}

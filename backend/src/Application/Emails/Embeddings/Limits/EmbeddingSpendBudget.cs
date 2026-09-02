// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Scheduling;

namespace MailFathom.Application.Emails.Embeddings.Limits;

/// <summary>The ceilings an instance is willing to spend on embedding, and the period they are counted over.</summary>
/// <remarks>
/// <para>
/// Embedding is the first thing MailFathom does that costs money per unit of mail, so what bounds it is a figure an
/// operator agreed to rather than one this system inferred. The unit is the characters actually sent to a provider,
/// because that is what every provider prices from — approximately, since a token is not a character — and because it
/// is the one quantity this deployment can count exactly without carrying a model's own tokenizer.
/// </para>
/// <para>
/// There are two ceilings and they answer different questions. The deployment's bounds the bill; an owner's bounds any
/// one person's share of it, so that a backfill of one large mailbox cannot exhaust the window everybody else is
/// working in. Both are counted over the same period, and a request is admitted only where both admit it.
/// </para>
/// <para>
/// The period is a fixed window anchored at the Unix epoch rather than a rolling one. A fixed window has a start an
/// operator can name, a roll-over instant work can wait for, and one row per period to count against; a rolling window
/// would need every spend event retained for the length of the window and would never give a paused worker a moment to
/// wake up at.
/// </para>
/// <para>
/// Configuration alone, and deliberately never part of an embedding profile.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// keeps the profile row to what a stored vector means; a ceiling decides how many vectors exist and changes the
/// meaning of none of them, so raising it re-embeds nothing.
/// </para>
/// </remarks>
public sealed class EmbeddingSpendBudget
{
    private EmbeddingSpendBudget(
        long maxInputCharactersPerPeriod,
        long maxInputCharactersPerPeriodPerOwner,
        TimeSpan period)
    {
        this.MaxInputCharactersPerPeriod = maxInputCharactersPerPeriod;
        this.MaxInputCharactersPerPeriodPerOwner = maxInputCharactersPerPeriodPerOwner;
        this.Period = period;
    }

    /// <summary>Gets a budget that bounds nothing, which is what an operator writing a ceiling of zero asked for.</summary>
    public static EmbeddingSpendBudget Unbounded { get; } = new(
        maxInputCharactersPerPeriod: 0,
        maxInputCharactersPerPeriodPerOwner: 0,
        TimeSpan.FromDays(1));

    /// <summary>Gets the characters one period may send to a provider in total, or zero where the operator declared no ceiling.</summary>
    public long MaxInputCharactersPerPeriod { get; }

    /// <summary>Gets the characters one period may send for any one owner, or zero where the operator declared no per-owner ceiling.</summary>
    public long MaxInputCharactersPerPeriodPerOwner { get; }

    /// <summary>Gets the length of the window the ceilings are counted over.</summary>
    public TimeSpan Period { get; }

    /// <summary>Gets whether this budget refuses nothing.</summary>
    public bool IsUnbounded => this.MaxInputCharactersPerPeriod == 0 && this.MaxInputCharactersPerPeriodPerOwner == 0;

    /// <summary>Builds a budget from what a deployment declared.</summary>
    /// <param name="maxInputCharactersPerPeriod">The characters one period may send in total, or zero for no ceiling at all.</param>
    /// <param name="maxInputCharactersPerPeriodPerOwner">The characters one period may send for any one owner, or zero for no per-owner ceiling.</param>
    /// <param name="period">The window the ceilings are counted over.</param>
    /// <returns>The budget.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either ceiling is negative, or the period is not positive.</exception>
    public static EmbeddingSpendBudget Create(
        long maxInputCharactersPerPeriod,
        long maxInputCharactersPerPeriodPerOwner,
        TimeSpan period)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxInputCharactersPerPeriod);
        ArgumentOutOfRangeException.ThrowIfNegative(maxInputCharactersPerPeriodPerOwner);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero);

        return maxInputCharactersPerPeriod == 0 && maxInputCharactersPerPeriodPerOwner == 0
            ? Unbounded
            : new EmbeddingSpendBudget(maxInputCharactersPerPeriod, maxInputCharactersPerPeriodPerOwner, period);
    }

    /// <summary>Finds the start of the period an instant falls in, which is the key the consumed total is counted under.</summary>
    /// <param name="instant">The moment to place in a period.</param>
    /// <returns>The period's start, in UTC.</returns>
    /// <remarks>
    /// Anchored at the Unix epoch so that every process of a deployment, and every restart of one, agrees on where a
    /// period begins without anything having to be stored to say so.
    /// </remarks>
    public DateTimeOffset PeriodStartAt(DateTimeOffset instant) => EpochAnchoredPeriod.StartAt(this.Period, instant);

    /// <summary>Finds when the period an instant falls in rolls over, which is when paused work resumes.</summary>
    /// <param name="instant">The moment to place in a period.</param>
    /// <returns>The instant the next period begins, in UTC.</returns>
    public DateTimeOffset PeriodEndAt(DateTimeOffset instant) => EpochAnchoredPeriod.EndAt(this.Period, instant);
}

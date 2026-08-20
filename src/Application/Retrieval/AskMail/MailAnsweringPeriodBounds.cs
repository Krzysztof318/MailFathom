// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Scheduling;

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>What every answering run of one period may add up to before the next question is refused.</summary>
/// <remarks>
/// <para>
/// The ceiling a per-run bound cannot state. <see cref="MailAnsweringRunBounds" /> makes one question cost a knowable
/// amount, and nothing about the MCP surface stops a client from asking a hundred of them in a minute — so without an
/// aggregate an instance's provider bill is a function of how enthusiastic its client is rather than of what its
/// operator agreed to.
/// </para>
/// <para>
/// Two ceilings over one period, for the reason a run has three: the run count is the one that always works, and the
/// token count is the one stated in what a provider bills. An endpoint that reports no usage leaves the second
/// unreachable, and an instance answering many cheap questions never approaches it while the first is exactly the
/// bound that applies.
/// </para>
/// <para>
/// The period is a fixed window anchored at the Unix epoch, which is the same placement
/// <see cref="Emails.Embeddings.Limits.EmbeddingSpendBudget" /> gives the other spend ceiling this product has: a fixed
/// window has a start an operator can name and a roll-over instant a refused caller can be told to come back at, while a
/// rolling one would need every run's timestamp kept for the length of the window. Anchoring it at the epoch is what
/// lets every restart of a process agree on where a period begins without anything being stored to say so.
/// </para>
/// <para>
/// What the fixed window costs is stated rather than hidden: a client that spends the whole ceiling at the end of one
/// window and again at the start of the next has spent twice the ceiling across an interval of the same length.
/// </para>
/// </remarks>
public sealed record MailAnsweringPeriodBounds
{
    private MailAnsweringPeriodBounds(TimeSpan period, int maximumRuns, long maximumTokens)
    {
        this.Period = period;
        this.MaximumRuns = maximumRuns;
        this.MaximumTokens = maximumTokens;
    }

    /// <summary>Gets the bounds a deployment that states none receives.</summary>
    /// <remarks>
    /// An hour rather than a day, because a ceiling an operator only meets once a day is one they meet after the spend
    /// has happened. Thirty questions and three hundred thousand tokens an hour is a person using their own mailbox
    /// heavily and is well below a client in a loop, which is exactly the line these are drawn at.
    /// </remarks>
    public static MailAnsweringPeriodBounds Default { get; } = new(TimeSpan.FromHours(1), 30, 300_000);

    /// <summary>Gets how long one period lasts before what was spent in it is forgotten.</summary>
    public TimeSpan Period { get; }

    /// <summary>Gets the greatest number of runs one period may admit.</summary>
    public int MaximumRuns { get; }

    /// <summary>Gets the greatest number of tokens the runs of one period may consume between them.</summary>
    public long MaximumTokens { get; }

    /// <summary>Creates bounds, refusing values no period could admit a question under.</summary>
    /// <param name="period">How long one period lasts.</param>
    /// <param name="maximumRuns">The greatest number of runs one period may admit.</param>
    /// <param name="maximumTokens">The greatest number of tokens the runs of one period may consume.</param>
    /// <returns>The validated bounds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the period is not positive, or a ceiling is below one.</exception>
    public static MailAnsweringPeriodBounds Create(TimeSpan period, int maximumRuns, long maximumTokens)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRuns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumTokens, 1);

        return new MailAnsweringPeriodBounds(period, maximumRuns, maximumTokens);
    }

    /// <summary>Finds the start of the period an instant falls in, which is the key its counts are held under.</summary>
    /// <param name="instant">The moment to place in a period.</param>
    /// <returns>The period's start, in UTC.</returns>
    /// <remarks>
    /// Anchored at the Unix epoch, so where a period begins is a function of the clock alone and every restart of the
    /// process places it identically.
    /// </remarks>
    public DateTimeOffset PeriodStartAt(DateTimeOffset instant) => EpochAnchoredPeriod.StartAt(this.Period, instant);

    /// <summary>Finds when the period an instant falls in rolls over, which is when a refused question is worth asking again.</summary>
    /// <param name="instant">The moment to place in a period.</param>
    /// <returns>The instant the next period begins, in UTC.</returns>
    public DateTimeOffset PeriodEndAt(DateTimeOffset instant) => EpochAnchoredPeriod.EndAt(this.Period, instant);

    /// <inheritdoc />
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "at most {0} runs costing at most {1} tokens every {2}",
        this.MaximumRuns,
        this.MaximumTokens,
        this.Period);
}

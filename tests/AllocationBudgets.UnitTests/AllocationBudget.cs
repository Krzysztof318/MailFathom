// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Xunit;

namespace MailFathom.AllocationBudgets.UnitTests;

/// <summary>Measures what one run of a streaming path allocates, and holds it to a stated upper bound.</summary>
/// <remarks>
/// <para>
/// The measurement is deterministic rather than statistical, which is what makes it usable as a gate: allocated bytes
/// are counted rather than timed, so a loaded runner changes the wall clock and changes nothing here. No assertion in
/// this suite reads a clock, and none may — <c>tests/AGENTS.md</c> holds why a timing claim belongs in the nightly
/// benchmark report instead.
/// </para>
/// <para>
/// What is asserted is a ceiling, never a number. A path that allocates less than its budget is not a failure and is
/// not a reason to lower the budget in the same change that made it cheaper: a bound exists so a regression fails, and
/// one tightened to whatever the last run happened to produce would fail on a runtime that pads a buffer differently.
/// </para>
/// </remarks>
internal static class AllocationBudget
{
    /// <summary>Runs the operation until it is warm and reports what the measured run allocated.</summary>
    /// <param name="operation">The path being measured, which must be safe to run several times.</param>
    /// <returns>The bytes the process allocated during the measured run.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The warm-up runs are what keep the number about the work rather than about reaching it: just-in-time
    /// compilation, static initialization, and the tables a library fills on its first call all allocate once and would
    /// otherwise be charged to whichever path ran first.
    /// </remarks>
    internal static async Task<long> MeasureAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await operation();
        await operation();

        var before = GC.GetTotalAllocatedBytes(precise: true);

        await operation();

        return GC.GetTotalAllocatedBytes(precise: true) - before;
    }

    /// <summary>Asserts that one run of the operation stays inside its budget.</summary>
    /// <param name="subject">What the budget is about, which the failure names.</param>
    /// <param name="budgetBytes">The greatest number of bytes one run may allocate.</param>
    /// <param name="operation">The path being measured.</param>
    /// <returns>A task that completes once the measured run has been taken and judged.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="subject" /> or <paramref name="operation" /> is <see langword="null" />.</exception>
    internal static async Task AssertWithinAsync(string subject, long budgetBytes, Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var allocatedBytes = await MeasureAsync(operation);

        Assert.True(
            allocatedBytes <= budgetBytes,
            $"{subject} allocated {allocatedBytes} bytes, which is above its budget of {budgetBytes} bytes.");
    }
}

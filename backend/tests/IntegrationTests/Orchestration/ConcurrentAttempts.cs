// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Xunit;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>What one operation's concurrent callers produced, and the assertion an idempotency claim is stated as.</summary>
/// <typeparam name="TResult">What one attempt answers with.</typeparam>
/// <param name="Operation">The operation the attempts ran, spelled the way a failure should report it.</param>
/// <param name="Attempted">How many callers ran it at once.</param>
/// <param name="Results">What the attempts that completed answered with, in no particular order.</param>
/// <param name="Failures">What the attempts that did not complete threw, which a refused duplicate is one of.</param>
internal sealed record ConcurrentAttempts<TResult>(
    string Operation,
    int Attempted,
    IReadOnlyList<TResult> Results,
    IReadOnlyList<Exception> Failures)
{
    /// <summary>Asserts that however many callers ran the operation at once, it left exactly one effect behind.</summary>
    /// <param name="observedEffects">How many effects the caller counted, which is the number a partial duplicate fails as.</param>
    /// <remarks>
    /// <para>
    /// The effect is a count rather than a presence, because two rows and seven rows say different things about how
    /// badly an operation is not idempotent, and an assertion of existence would report both as the same failure.
    /// </para>
    /// <para>
    /// An attempt that failed is named beside the count rather than treated as an error of the run: a duplicate refused
    /// by a unique index is how the database enforces several of these claims. A run where every attempt failed is the
    /// one case that is a defect on its own, because a single effect nobody's attempt produced was left there by the
    /// arrangement and proves nothing about the operation.
    /// </para>
    /// </remarks>
    internal void AssertSingleEffect(int observedEffects)
    {
        if (this.Results.Count == 0)
        {
            Assert.Fail(
                $"{this.Operation} left {observedEffects} effect(s), but none of its {this.Attempted} concurrent "
                + $"attempts completed, so nothing was proved about it. {this.DescribeFailures()}");
        }

        if (observedEffects != 1)
        {
            Assert.Fail(
                $"{this.Operation} left {observedEffects} effect(s) rather than exactly one after {this.Attempted} "
                + $"concurrent attempts, of which {this.Results.Count} completed. {this.DescribeFailures()}");
        }
    }

    private string DescribeFailures() => this.Failures.Count == 0
        ? "No attempt failed."
        : $"{this.Failures.Count} failed: {string.Join(
            "; ",
            this.Failures.Select(failure => $"{failure.GetType().Name}: {failure.Message}").Distinct())}";
}

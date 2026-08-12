// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules.History;

/// <summary>Identifies one recorded rule execution: what one rule concluded about one email, on one pass.</summary>
/// <remarks>
/// Its own identity rather than the pair of the rule and the email, because the same rule reaches the same email again
/// whenever a whole-mailbox run is asked for, and each of those readings is a decision of its own. It is also what a
/// continuation cursor names to break a tie between two executions recorded in the same instant.
/// </remarks>
public readonly record struct MailRuleExecutionId
{
    private MailRuleExecutionId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Creates an execution identifier from a non-empty UUID.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated execution identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static MailRuleExecutionId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A mail rule execution identifier cannot be empty.", nameof(value));
        }

        return new MailRuleExecutionId(value);
    }

    /// <summary>Creates the identifier of an execution being recorded for the first time.</summary>
    /// <returns>A fresh identifier.</returns>
    /// <remarks>
    /// Version 7 rather than version 4, so the identifier a tie is broken by rises with the instant the execution was
    /// recorded at. The index the history is read through is ordered by the pair, and an identifier ordered at random
    /// would scatter one batch's inserts across it.
    /// </remarks>
    public static MailRuleExecutionId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}

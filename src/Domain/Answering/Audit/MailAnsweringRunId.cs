// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Answering.Audit;

/// <summary>Identifies one answering run, across every account whose mailbox it was asked about.</summary>
/// <remarks>
/// A run is recorded once per account in its scope, because enabling the record, its retention, and its erasure are all
/// decisions one account's operator makes. This is what says those entries describe one question rather than several,
/// and it is what makes a repeated append leave the trail as it was: one entry per run per account.
/// </remarks>
public readonly record struct MailAnsweringRunId
{
    private MailAnsweringRunId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Creates a run identifier from a non-empty UUID.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated run identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static MailAnsweringRunId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A mail answering run identifier cannot be empty.", nameof(value));
        }

        return new MailAnsweringRunId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}

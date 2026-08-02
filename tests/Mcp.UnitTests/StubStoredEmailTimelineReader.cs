// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails;
using MailFathom.Domain.Emails;

namespace MailFathom.Mcp.UnitTests;

/// <summary>Answers a timeline read with a fixed page and records what the use case asked for.</summary>
/// <remarks>
/// The tool under test reaches storage through the real use case, so the filter this stub receives is the one the
/// arguments were normalized into. That is what a test asserts against: the tool owns the conversion, and the validated
/// filter is the observable result of it.
/// </remarks>
internal sealed class StubStoredEmailTimelineReader(params EmailSummary[] page) : IStoredEmailTimelineReader
{
    /// <summary>Gets the filter the last read was issued with, or <see langword="null" /> when nothing was read.</summary>
    public EmailTimelineFilter? LastFilter { get; private set; }

    /// <summary>Gets the position the last read continued after.</summary>
    public EmailTimelinePosition? LastContinueAfter { get; private set; }

    /// <summary>Gets the row limit the last read asked for, which is the page size plus the row that establishes a next page.</summary>
    public int LastLimit { get; private set; }

    /// <summary>Gets how many reads were issued, so a test can prove a refusal never reached storage.</summary>
    public int ReadCount { get; private set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<EmailSummary>> ReadPageAsync(
        EmailTimelineFilter filter,
        EmailTimelinePosition? continueAfter,
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        this.LastFilter = filter;
        this.LastContinueAfter = continueAfter;
        this.LastLimit = limit;
        this.ReadCount++;

        return Task.FromResult<IReadOnlyList<EmailSummary>>([.. page.Take(limit)]);
    }
}

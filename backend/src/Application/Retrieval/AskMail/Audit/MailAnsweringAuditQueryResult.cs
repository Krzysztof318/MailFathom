// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Retrieval.AskMail.Audit;

/// <summary>States whether a request named a page of an answering record, and what stopped it when it did not.</summary>
public enum MailAnsweringAuditQueryOutcome
{
    /// <summary>The request names a page, and the query is present.</summary>
    Accepted = 0,

    /// <summary>The page size asked for lies outside the range the record serves.</summary>
    PageSizeOutOfRange = 1,

    /// <summary>The time range ends at or before it begins, so it names no entries.</summary>
    TimeRangeEmpty = 2,

    /// <summary>The continuation cursor was issued for a different set of filters than the request carries.</summary>
    CursorFilterMismatch = 3,
}

/// <summary>Carries the query a request named, or the reason it named none.</summary>
/// <remarks>
/// A refusal is a result rather than an exception because the immediate caller acts on it and continues: the
/// administrative endpoint turns it into a <c>400</c> naming what the caller has to change, and nothing above that has
/// to decide what a malformed page request means.
/// </remarks>
public sealed record MailAnsweringAuditQueryResult
{
    private MailAnsweringAuditQueryResult(
        MailAnsweringAuditQueryOutcome outcome,
        MailAnsweringAuditQuery? query)
    {
        this.Outcome = outcome;
        this.Query = query;
    }

    /// <summary>Gets what happened.</summary>
    public MailAnsweringAuditQueryOutcome Outcome { get; }

    /// <summary>Gets the query, which is present exactly when <see cref="Outcome" /> is <see cref="MailAnsweringAuditQueryOutcome.Accepted" />.</summary>
    public MailAnsweringAuditQuery? Query { get; }

    /// <summary>Reports a request that names a page.</summary>
    /// <param name="query">The validated query.</param>
    /// <returns>An accepted result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    public static MailAnsweringAuditQueryResult Accepted(MailAnsweringAuditQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return new MailAnsweringAuditQueryResult(MailAnsweringAuditQueryOutcome.Accepted, query);
    }

    /// <summary>Reports a request that names no page.</summary>
    /// <param name="outcome">What the caller has to change.</param>
    /// <returns>A refused result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="outcome" /> names an acceptance rather than a refusal.</exception>
    public static MailAnsweringAuditQueryResult Refused(MailAnsweringAuditQueryOutcome outcome)
    {
        if (outcome == MailAnsweringAuditQueryOutcome.Accepted || !Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "A refused answering audit query must name a declared refusal.");
        }

        return new MailAnsweringAuditQueryResult(outcome, query: null);
    }
}

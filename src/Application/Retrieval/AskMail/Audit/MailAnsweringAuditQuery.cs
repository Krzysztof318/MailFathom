// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Paging;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Retrieval.AskMail.Audit;

/// <summary>Asks one bounded, keyset-paginated page of an account's answering record.</summary>
/// <remarks>
/// <para>
/// The account is required rather than optional, and every other filter narrows within it. Enabling the record, its
/// retention, and its erasure are all decisions one account's operator makes, so reading it is scoped the same way — and
/// a surface over derived personal data is better for making a caller name whose mailbox they are asking about than for
/// serving a deployment-wide list nobody asked a question about.
/// </para>
/// <para>
/// A page is always bounded and always ordered newest first, so a caller that supplies nothing gets the most recent
/// <see cref="DefaultPageSize" /> entries rather than the whole record.
/// </para>
/// </remarks>
public sealed record MailAnsweringAuditQuery
{
    /// <summary>The page size a request that names none is served.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>The greatest page size one request may ask for.</summary>
    /// <remarks>
    /// Smaller than the mutation trail's, because an entry here carries a list of the emails one run read rather than a
    /// fixed set of columns. A page is bounded by what it returns rather than only by how many rows it names.
    /// </remarks>
    public const int MaximumPageSize = 100;

    private MailAnsweringAuditQuery(
        MailAccountId accountId,
        DateTimeOffset? completedFrom,
        DateTimeOffset? completedBefore,
        int pageSize,
        MailAnsweringAuditCursor? cursor)
    {
        this.AccountId = accountId;
        this.CompletedFrom = completedFrom;
        this.CompletedBefore = completedBefore;
        this.PageSize = pageSize;
        this.Cursor = cursor;
    }

    /// <summary>Gets the account whose record is read.</summary>
    public MailAccountId AccountId { get; }

    /// <summary>Gets the earliest completion instant served, inclusive, or <see langword="null" /> when the page reaches back as far as the record does.</summary>
    public DateTimeOffset? CompletedFrom { get; }

    /// <summary>Gets the completion instant the page stops before, exclusive, or <see langword="null" /> when it reaches the newest entry.</summary>
    public DateTimeOffset? CompletedBefore { get; }

    /// <summary>Gets how many entries the page holds at most.</summary>
    public int PageSize { get; }

    /// <summary>Gets the boundary a continued walk reads beyond, or <see langword="null" /> for the first page.</summary>
    public MailAnsweringAuditCursor? Cursor { get; }

    /// <summary>Gets the fingerprint of the filters this query reads under, which its cursors are issued against.</summary>
    /// <remarks>
    /// The page size is deliberately not part of it. A caller may ask for a shorter or longer page while continuing the
    /// same walk, and refusing that would be a rule about pacing rather than about which entries the boundary sits in.
    /// </remarks>
    public string FilterFingerprint => ComputeFingerprint(
        this.AccountId,
        this.CompletedFrom,
        this.CompletedBefore);

    /// <summary>Builds a validated query from what a caller asked for, or reports why the request names no page.</summary>
    /// <param name="accountId">The account whose record is read.</param>
    /// <param name="completedFrom">The earliest completion instant served, inclusive, or <see langword="null" /> for none.</param>
    /// <param name="completedBefore">The completion instant to stop before, exclusive, or <see langword="null" /> for none.</param>
    /// <param name="pageSize">How many entries the page may hold, or <see langword="null" /> for <see cref="DefaultPageSize" />.</param>
    /// <param name="cursor">The boundary a continued walk reads beyond, or <see langword="null" /> for the first page.</param>
    /// <returns>The accepted query, or the refusal naming what the caller has to change.</returns>
    public static MailAnsweringAuditQueryResult Create(
        MailAccountId accountId,
        DateTimeOffset? completedFrom,
        DateTimeOffset? completedBefore,
        int? pageSize,
        MailAnsweringAuditCursor? cursor)
    {
        var resolvedPageSize = pageSize ?? DefaultPageSize;

        if (resolvedPageSize is < 1 or > MaximumPageSize)
        {
            return MailAnsweringAuditQueryResult.Refused(MailAnsweringAuditQueryOutcome.PageSizeOutOfRange);
        }

        if (completedFrom is { } from && completedBefore is { } before && from >= before)
        {
            return MailAnsweringAuditQueryResult.Refused(MailAnsweringAuditQueryOutcome.TimeRangeEmpty);
        }

        var query = new MailAnsweringAuditQuery(
            accountId,
            completedFrom,
            completedBefore,
            resolvedPageSize,
            cursor);

        if (cursor is { } presentedCursor
            && !string.Equals(presentedCursor.FilterFingerprint, query.FilterFingerprint, StringComparison.Ordinal))
        {
            return MailAnsweringAuditQueryResult.Refused(MailAnsweringAuditQueryOutcome.CursorFilterMismatch);
        }

        return MailAnsweringAuditQueryResult.Accepted(query);
    }

    /// <summary>Reduces the filters to the short stable text a cursor carries to prove it belongs to this walk.</summary>
    private static string ComputeFingerprint(
        MailAccountId accountId,
        DateTimeOffset? completedFrom,
        DateTimeOffset? completedBefore) =>
        PageFilterFingerprint.Of(
            accountId.Value,
            completedFrom?.UtcTicks.ToString(CultureInfo.InvariantCulture),
            completedBefore?.UtcTicks.ToString(CultureInfo.InvariantCulture));
}

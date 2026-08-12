// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam.History;

/// <summary>Asks one bounded, keyset-paginated page of what classification concluded about an account's mail.</summary>
/// <remarks>
/// <para>
/// The account is required rather than optional, and every other filter narrows within it. Retention and erasure are
/// decisions made per account, so reading is scoped the same way — and a surface over derived personal data is better
/// for making a caller name whose mailbox they are asking about than for serving a deployment-wide list.
/// </para>
/// <para>
/// The three optional filters are the three ways an operator arrives here: the verdict answers what a run decided over a
/// mailbox, the message answers why one message was filed, and the time range answers what one run concluded, since a
/// run is bounded by when it started and when it ended. They compose rather than exclude.
/// </para>
/// <para>
/// A page is always bounded and always ordered newest first, so a caller that supplies nothing gets the most recent
/// <see cref="DefaultPageSize" /> classifications rather than every message of the mailbox.
/// </para>
/// </remarks>
public sealed record SpamClassificationHistoryQuery
{
    /// <summary>The page size a request that names none is served.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>The greatest page size one request may ask for.</summary>
    /// <remarks>
    /// A classification carries the names of every signal the stages produced and the changes the verdict asked for, so a
    /// page is bounded by what it returns rather than only by how many rows it names. Both of those are themselves
    /// bounded per record, which is what keeps this figure meaningful.
    /// </remarks>
    public const int MaximumPageSize = 200;

    /// <summary>Separates the fingerprint's fields, chosen because no filter value can contain it.</summary>
    private const char FingerprintFieldSeparator = '\u001f';

    /// <summary>How many hexadecimal characters of the filter digest a cursor carries.</summary>
    /// <remarks>
    /// Short because it distinguishes one caller's own filter sets rather than resisting a search for a collision: a
    /// forged fingerprint buys a boundary inside a page that same caller is already entitled to read.
    /// </remarks>
    private const int FingerprintLength = 16;

    private SpamClassificationHistoryQuery(
        MailAccountId accountId,
        StoredEmailId? storedEmailId,
        SpamVerdict? verdict,
        DateTimeOffset? evaluatedFrom,
        DateTimeOffset? evaluatedBefore,
        int pageSize,
        SpamClassificationHistoryCursor? cursor)
    {
        this.AccountId = accountId;
        this.StoredEmailId = storedEmailId;
        this.Verdict = verdict;
        this.EvaluatedFrom = evaluatedFrom;
        this.EvaluatedBefore = evaluatedBefore;
        this.PageSize = pageSize;
        this.Cursor = cursor;
    }

    /// <summary>Gets the account whose classifications are read.</summary>
    public MailAccountId AccountId { get; }

    /// <summary>Gets the occurrence the page is narrowed to, or <see langword="null" /> for every occurrence of the account.</summary>
    public StoredEmailId? StoredEmailId { get; }

    /// <summary>Gets the verdict the page is narrowed to, or <see langword="null" /> for every verdict.</summary>
    public SpamVerdict? Verdict { get; }

    /// <summary>Gets the earliest evaluation instant served, inclusive, or <see langword="null" /> when the page reaches as far back as the records do.</summary>
    public DateTimeOffset? EvaluatedFrom { get; }

    /// <summary>Gets the evaluation instant the page stops before, exclusive, or <see langword="null" /> when it reaches the newest record.</summary>
    public DateTimeOffset? EvaluatedBefore { get; }

    /// <summary>Gets how many classifications the page holds at most.</summary>
    public int PageSize { get; }

    /// <summary>Gets the boundary a continued walk reads beyond, or <see langword="null" /> for the first page.</summary>
    public SpamClassificationHistoryCursor? Cursor { get; }

    /// <summary>Gets the fingerprint of the filters this query reads under, which its cursors are issued against.</summary>
    /// <remarks>
    /// The page size is deliberately not part of it. A caller may ask for a shorter or longer page while continuing the
    /// same walk, and refusing that would be a rule about pacing rather than about which records the boundary sits in.
    /// </remarks>
    public string FilterFingerprint => ComputeFingerprint(
        this.AccountId,
        this.StoredEmailId,
        this.Verdict,
        this.EvaluatedFrom,
        this.EvaluatedBefore);

    /// <summary>Builds a validated query from what a caller asked for, or reports why the request names no page.</summary>
    /// <param name="accountId">The account whose classifications are read.</param>
    /// <param name="storedEmailId">The occurrence to narrow to, or <see langword="null" /> for every occurrence.</param>
    /// <param name="verdict">The verdict to narrow to, or <see langword="null" /> for every verdict.</param>
    /// <param name="evaluatedFrom">The earliest evaluation instant served, inclusive, or <see langword="null" /> for none.</param>
    /// <param name="evaluatedBefore">The evaluation instant to stop before, exclusive, or <see langword="null" /> for none.</param>
    /// <param name="pageSize">How many classifications the page may hold, or <see langword="null" /> for <see cref="DefaultPageSize" />.</param>
    /// <param name="cursor">The boundary a continued walk reads beyond, or <see langword="null" /> for the first page.</param>
    /// <returns>The accepted query, or the refusal naming what the caller has to change.</returns>
    public static SpamClassificationHistoryQueryResult Create(
        MailAccountId accountId,
        StoredEmailId? storedEmailId,
        SpamVerdict? verdict,
        DateTimeOffset? evaluatedFrom,
        DateTimeOffset? evaluatedBefore,
        int? pageSize,
        SpamClassificationHistoryCursor? cursor)
    {
        var resolvedPageSize = pageSize ?? DefaultPageSize;

        if (resolvedPageSize is < 1 or > MaximumPageSize)
        {
            return SpamClassificationHistoryQueryResult.Refused(
                SpamClassificationHistoryQueryOutcome.PageSizeOutOfRange);
        }

        if (verdict is { } named && !Enum.IsDefined(named))
        {
            return SpamClassificationHistoryQueryResult.Refused(
                SpamClassificationHistoryQueryOutcome.VerdictUnknown);
        }

        if (evaluatedFrom is { } from && evaluatedBefore is { } before && from >= before)
        {
            return SpamClassificationHistoryQueryResult.Refused(
                SpamClassificationHistoryQueryOutcome.TimeRangeEmpty);
        }

        var query = new SpamClassificationHistoryQuery(
            accountId,
            storedEmailId,
            verdict,
            evaluatedFrom,
            evaluatedBefore,
            resolvedPageSize,
            cursor);

        if (cursor is { } presentedCursor
            && !string.Equals(presentedCursor.FilterFingerprint, query.FilterFingerprint, StringComparison.Ordinal))
        {
            return SpamClassificationHistoryQueryResult.Refused(
                SpamClassificationHistoryQueryOutcome.CursorFilterMismatch);
        }

        return SpamClassificationHistoryQueryResult.Accepted(query);
    }

    /// <summary>Reduces the filters to the short stable text a cursor carries to prove it belongs to this walk.</summary>
    private static string ComputeFingerprint(
        MailAccountId accountId,
        StoredEmailId? storedEmailId,
        SpamVerdict? verdict,
        DateTimeOffset? evaluatedFrom,
        DateTimeOffset? evaluatedBefore)
    {
        var material = string.Join(
            FingerprintFieldSeparator,
            accountId.Value,
            storedEmailId?.Value.ToString("N", CultureInfo.InvariantCulture) ?? string.Empty,
            verdict?.ToString() ?? string.Empty,
            evaluatedFrom?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            evaluatedBefore?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..FingerprintLength];
    }
}

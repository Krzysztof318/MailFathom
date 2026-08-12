// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Rules.History;

/// <summary>Asks one bounded, keyset-paginated page of an account's rule history.</summary>
/// <remarks>
/// <para>
/// The account is required rather than optional, and every other filter narrows within it. Retention and erasure are
/// decisions made per account, so reading is scoped the same way — and a surface over derived personal data is better
/// for making a caller name whose mailbox they are asking about than for serving a deployment-wide list.
/// </para>
/// <para>
/// The two optional identities are the two ways the history is arrived at, and they compose rather than exclude: naming
/// a rule answers what that rule has been doing, naming a message answers why that message was filed, and naming both
/// answers what one rule concluded about one message across the runs that reached it.
/// </para>
/// <para>
/// A page is always bounded and always ordered newest first, so a caller that supplies nothing gets the most recent
/// <see cref="DefaultPageSize" /> executions rather than the whole history.
/// </para>
/// </remarks>
public sealed record MailRuleExecutionQuery
{
    /// <summary>The page size a request that names none is served.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>The greatest page size one request may ask for.</summary>
    /// <remarks>
    /// An execution carries the actions its rule declared and the facts its condition read, so a page is bounded by what
    /// it returns rather than only by how many rows it names. Both of those are themselves bounded by the rule set, which
    /// is what keeps this figure meaningful.
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

    private MailRuleExecutionQuery(
        MailAccountId accountId,
        string? ruleName,
        StoredEmailId? storedEmailId,
        DateTimeOffset? evaluatedFrom,
        DateTimeOffset? evaluatedBefore,
        int pageSize,
        MailRuleExecutionCursor? cursor)
    {
        this.AccountId = accountId;
        this.RuleName = ruleName;
        this.StoredEmailId = storedEmailId;
        this.EvaluatedFrom = evaluatedFrom;
        this.EvaluatedBefore = evaluatedBefore;
        this.PageSize = pageSize;
        this.Cursor = cursor;
    }

    /// <summary>Gets the account whose history is read.</summary>
    public MailAccountId AccountId { get; }

    /// <summary>Gets the rule the page is narrowed to, or <see langword="null" /> for every rule of the account.</summary>
    public string? RuleName { get; }

    /// <summary>Gets the email the page is narrowed to, or <see langword="null" /> for every email of the account.</summary>
    public StoredEmailId? StoredEmailId { get; }

    /// <summary>Gets the earliest evaluation instant served, inclusive, or <see langword="null" /> when the page reaches as far back as the history does.</summary>
    public DateTimeOffset? EvaluatedFrom { get; }

    /// <summary>Gets the evaluation instant the page stops before, exclusive, or <see langword="null" /> when it reaches the newest execution.</summary>
    public DateTimeOffset? EvaluatedBefore { get; }

    /// <summary>Gets how many executions the page holds at most.</summary>
    public int PageSize { get; }

    /// <summary>Gets the boundary a continued walk reads beyond, or <see langword="null" /> for the first page.</summary>
    public MailRuleExecutionCursor? Cursor { get; }

    /// <summary>Gets the fingerprint of the filters this query reads under, which its cursors are issued against.</summary>
    /// <remarks>
    /// The page size is deliberately not part of it. A caller may ask for a shorter or longer page while continuing the
    /// same walk, and refusing that would be a rule about pacing rather than about which executions the boundary sits in.
    /// </remarks>
    public string FilterFingerprint => ComputeFingerprint(
        this.AccountId,
        this.RuleName,
        this.StoredEmailId,
        this.EvaluatedFrom,
        this.EvaluatedBefore);

    /// <summary>Builds a validated query from what a caller asked for, or reports why the request names no page.</summary>
    /// <param name="accountId">The account whose history is read.</param>
    /// <param name="ruleName">The rule to narrow to, or <see langword="null" /> for every rule.</param>
    /// <param name="storedEmailId">The email to narrow to, or <see langword="null" /> for every email.</param>
    /// <param name="evaluatedFrom">The earliest evaluation instant served, inclusive, or <see langword="null" /> for none.</param>
    /// <param name="evaluatedBefore">The evaluation instant to stop before, exclusive, or <see langword="null" /> for none.</param>
    /// <param name="pageSize">How many executions the page may hold, or <see langword="null" /> for <see cref="DefaultPageSize" />.</param>
    /// <param name="cursor">The boundary a continued walk reads beyond, or <see langword="null" /> for the first page.</param>
    /// <returns>The accepted query, or the refusal naming what the caller has to change.</returns>
    public static MailRuleExecutionQueryResult Create(
        MailAccountId accountId,
        string? ruleName,
        StoredEmailId? storedEmailId,
        DateTimeOffset? evaluatedFrom,
        DateTimeOffset? evaluatedBefore,
        int? pageSize,
        MailRuleExecutionCursor? cursor)
    {
        var resolvedPageSize = pageSize ?? DefaultPageSize;

        if (resolvedPageSize is < 1 or > MaximumPageSize)
        {
            return MailRuleExecutionQueryResult.Refused(MailRuleExecutionQueryOutcome.PageSizeOutOfRange);
        }

        // A rule filter that is present but blank is a caller who meant to name one and did not, rather than a caller
        // asking for every rule. Reading it as the second would answer a different question than the one asked.
        if (ruleName is not null && string.IsNullOrWhiteSpace(ruleName))
        {
            return MailRuleExecutionQueryResult.Refused(MailRuleExecutionQueryOutcome.RuleNameBlank);
        }

        if (evaluatedFrom is { } from && evaluatedBefore is { } before && from >= before)
        {
            return MailRuleExecutionQueryResult.Refused(MailRuleExecutionQueryOutcome.TimeRangeEmpty);
        }

        var query = new MailRuleExecutionQuery(
            accountId,
            ruleName?.Trim(),
            storedEmailId,
            evaluatedFrom,
            evaluatedBefore,
            resolvedPageSize,
            cursor);

        if (cursor is { } presentedCursor
            && !string.Equals(presentedCursor.FilterFingerprint, query.FilterFingerprint, StringComparison.Ordinal))
        {
            return MailRuleExecutionQueryResult.Refused(MailRuleExecutionQueryOutcome.CursorFilterMismatch);
        }

        return MailRuleExecutionQueryResult.Accepted(query);
    }

    /// <summary>Reduces the filters to the short stable text a cursor carries to prove it belongs to this walk.</summary>
    private static string ComputeFingerprint(
        MailAccountId accountId,
        string? ruleName,
        StoredEmailId? storedEmailId,
        DateTimeOffset? evaluatedFrom,
        DateTimeOffset? evaluatedBefore)
    {
        var material = string.Join(
            FingerprintFieldSeparator,
            accountId.Value,
            ruleName ?? string.Empty,
            storedEmailId?.Value.ToString("N", CultureInfo.InvariantCulture) ?? string.Empty,
            evaluatedFrom?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            evaluatedBefore?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..FingerprintLength];
    }
}

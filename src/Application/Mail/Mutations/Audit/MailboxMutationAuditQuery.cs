// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations.Audit;

/// <summary>Asks one bounded, keyset-paginated page of an account's audit trail.</summary>
/// <remarks>
/// <para>
/// The account is required rather than optional, and every other filter narrows within it. Enabling the trail, its
/// retention, and its erasure are all decisions one account's operator makes, so reading it is scoped the same way — and
/// a surface over derived personal data is better for making a caller name whose mail they are looking at than for
/// serving a deployment-wide list nobody asked a question about.
/// </para>
/// <para>
/// A page is always bounded and always ordered newest first, so a caller that supplies nothing gets the most recent
/// <see cref="DefaultPageSize" /> entries rather than the whole trail.
/// </para>
/// </remarks>
public sealed record MailboxMutationAuditQuery
{
    /// <summary>The page size a request that names none is served.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>The greatest page size one request may ask for.</summary>
    public const int MaximumPageSize = 200;

    /// <summary>Separates the fingerprint's fields, chosen because no filter value can contain it.</summary>
    private const char FingerprintFieldSeparator = '\u001f';

    /// <summary>How many hexadecimal characters of the filter digest a cursor carries.</summary>
    /// <remarks>
    /// Short because it distinguishes one caller's own filter sets rather than resisting a search for a collision: a
    /// forged fingerprint buys a boundary inside a page that same caller is already entitled to read.
    /// </remarks>
    private const int FingerprintLength = 16;

    private MailboxMutationAuditQuery(
        MailAccountId accountId,
        MailboxMutation mutation,
        DateTimeOffset? completedFrom,
        DateTimeOffset? completedBefore,
        int pageSize,
        MailboxMutationAuditCursor? cursor)
    {
        this.AccountId = accountId;
        this.Mutation = mutation;
        this.CompletedFrom = completedFrom;
        this.CompletedBefore = completedBefore;
        this.PageSize = pageSize;
        this.Cursor = cursor;
    }

    /// <summary>Gets the account whose trail is read.</summary>
    public MailAccountId AccountId { get; }

    /// <summary>Gets the mutation the page is narrowed to, or the unspecified default when every mutation is served.</summary>
    public MailboxMutation Mutation { get; }

    /// <summary>Gets the earliest completion instant served, inclusive, or <see langword="null" /> when the page reaches back as far as the trail does.</summary>
    public DateTimeOffset? CompletedFrom { get; }

    /// <summary>Gets the completion instant the page stops before, exclusive, or <see langword="null" /> when it reaches the newest entry.</summary>
    public DateTimeOffset? CompletedBefore { get; }

    /// <summary>Gets how many entries the page holds at most.</summary>
    public int PageSize { get; }

    /// <summary>Gets the boundary a continued walk reads beyond, or <see langword="null" /> for the first page.</summary>
    public MailboxMutationAuditCursor? Cursor { get; }

    /// <summary>Gets the fingerprint of the filters this query reads under, which its cursors are issued against.</summary>
    /// <remarks>
    /// The page size is deliberately not part of it. A caller may ask for a shorter or longer page while continuing the
    /// same walk, and refusing that would be a rule about pacing rather than about which entries the boundary sits in.
    /// </remarks>
    public string FilterFingerprint => ComputeFingerprint(
        this.AccountId,
        this.Mutation,
        this.CompletedFrom,
        this.CompletedBefore);

    /// <summary>Builds a validated query from what a caller asked for, or reports why the request names no page.</summary>
    /// <param name="accountId">The account whose trail is read.</param>
    /// <param name="mutation">The mutation to narrow to, or the unspecified default for every mutation.</param>
    /// <param name="completedFrom">The earliest completion instant served, inclusive, or <see langword="null" /> for none.</param>
    /// <param name="completedBefore">The completion instant to stop before, exclusive, or <see langword="null" /> for none.</param>
    /// <param name="pageSize">How many entries the page may hold, or <see langword="null" /> for <see cref="DefaultPageSize" />.</param>
    /// <param name="cursor">The boundary a continued walk reads beyond, or <see langword="null" /> for the first page.</param>
    /// <returns>The accepted query, or the refusal naming what the caller has to change.</returns>
    public static MailboxMutationAuditQueryResult Create(
        MailAccountId accountId,
        MailboxMutation mutation,
        DateTimeOffset? completedFrom,
        DateTimeOffset? completedBefore,
        int? pageSize,
        MailboxMutationAuditCursor? cursor)
    {
        var resolvedPageSize = pageSize ?? DefaultPageSize;

        if (resolvedPageSize is < 1 or > MaximumPageSize)
        {
            return MailboxMutationAuditQueryResult.Refused(MailboxMutationAuditQueryOutcome.PageSizeOutOfRange);
        }

        if (completedFrom is { } from && completedBefore is { } before && from >= before)
        {
            return MailboxMutationAuditQueryResult.Refused(MailboxMutationAuditQueryOutcome.TimeRangeEmpty);
        }

        var query = new MailboxMutationAuditQuery(
            accountId,
            mutation,
            completedFrom,
            completedBefore,
            resolvedPageSize,
            cursor);

        if (cursor is { } presentedCursor
            && !string.Equals(presentedCursor.FilterFingerprint, query.FilterFingerprint, StringComparison.Ordinal))
        {
            return MailboxMutationAuditQueryResult.Refused(MailboxMutationAuditQueryOutcome.CursorFilterMismatch);
        }

        return MailboxMutationAuditQueryResult.Accepted(query);
    }

    /// <summary>Reduces the filters to the short stable text a cursor carries to prove it belongs to this walk.</summary>
    private static string ComputeFingerprint(
        MailAccountId accountId,
        MailboxMutation mutation,
        DateTimeOffset? completedFrom,
        DateTimeOffset? completedBefore)
    {
        var material = string.Join(
            FingerprintFieldSeparator,
            accountId.Value,
            mutation.IsSpecified ? mutation.Name : string.Empty,
            completedFrom?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            completedBefore?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..FingerprintLength];
    }
}

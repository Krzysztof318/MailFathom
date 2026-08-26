// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Paging;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Jobs.DeadLetters;

/// <summary>Asks one bounded, keyset-paginated page of the jobs nothing will attempt again.</summary>
/// <remarks>
/// <para>
/// Both filters are optional, and neither is an account this time. A dead letter is a fact about the deployment rather
/// than about somebody's mailbox — what it names is a job type and an idempotency key composed of MailFathom's own
/// aliases — so the reading is deployment-wide by default, which is what makes "what has stopped" one question rather
/// than one per configured account. Narrowing to an account is still offered, because a failure that reaches only one
/// mailbox is the ordinary shape of a credential that expired.
/// </para>
/// <para>
/// A page is always bounded and always ordered newest first, so a caller that supplies nothing gets the most recently
/// stopped <see cref="DefaultPageSize" /> jobs rather than every job that ever stopped.
/// </para>
/// </remarks>
public sealed record DeadLetteredJobQuery
{
    /// <summary>The page size a request that names none is served.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>The greatest page size one request may ask for.</summary>
    /// <remarks>
    /// Every field of a record is itself bounded — a type name, a key of at most
    /// <see cref="JobIdempotencyKey.MaximumLength" /> characters, a reason of at most
    /// <see cref="JobFailureRecord.MaximumReasonLength" /> — so this figure bounds what the answer weighs as well as how
    /// many rows it names.
    /// </remarks>
    public const int MaximumPageSize = 200;

    private DeadLetteredJobQuery(
        JobType? jobType,
        MailAccountIdentity? account,
        int pageSize,
        DeadLetteredJobCursor? cursor)
    {
        this.JobType = jobType;
        this.Account = account;
        this.PageSize = pageSize;
        this.Cursor = cursor;
    }

    /// <summary>Gets the kind of work the page is narrowed to, or <see langword="null" /> for every kind.</summary>
    public JobType? JobType { get; }

    /// <summary>Gets the account the page is narrowed to, or <see langword="null" /> for every account and for work belonging to none.</summary>
    public MailAccountIdentity? Account { get; }
    /// <summary>Gets the identifier half of <see cref="Account" />, or <see langword="null" /> when the reading is across every account.</summary>
    public MailAccountId? AccountId => this.Account?.Id;

    /// <summary>Gets how many jobs the page holds at most.</summary>
    public int PageSize { get; }

    /// <summary>Gets the boundary a continued walk reads beyond, or <see langword="null" /> for the first page.</summary>
    public DeadLetteredJobCursor? Cursor { get; }

    /// <summary>Gets the fingerprint of the filters this query reads under, which its cursors are issued against.</summary>
    /// <remarks>
    /// The page size is deliberately not part of it. A caller may ask for a shorter or longer page while continuing the
    /// same walk, and refusing that would be a rule about pacing rather than about which records the boundary sits in.
    /// </remarks>
    public string FilterFingerprint => ComputeFingerprint(this.JobType, this.Account);

    /// <summary>Builds a validated query from what a caller asked for, or reports why the request names no page.</summary>
    /// <param name="jobType">The kind of work to narrow to, or <see langword="null" /> for every kind.</param>
    /// <param name="account">The account to narrow to, or <see langword="null" /> for every account.</param>
    /// <param name="pageSize">How many jobs the page may hold, or <see langword="null" /> for <see cref="DefaultPageSize" />.</param>
    /// <param name="cursor">The boundary a continued walk reads beyond, or <see langword="null" /> for the first page.</param>
    /// <returns>The accepted query, or the refusal naming what the caller has to change.</returns>
    public static DeadLetteredJobQueryResult Create(
        JobType? jobType,
        MailAccountIdentity? account,
        int? pageSize,
        DeadLetteredJobCursor? cursor)
    {
        var resolvedPageSize = pageSize ?? DefaultPageSize;

        if (resolvedPageSize is < 1 or > MaximumPageSize)
        {
            return DeadLetteredJobQueryResult.Refused(DeadLetteredJobQueryOutcome.PageSizeOutOfRange);
        }

        // The struct default names no type, so a caller that built one by hand would otherwise filter on a name that
        // cannot be read back out of the value.
        if (jobType is { IsSpecified: false })
        {
            return DeadLetteredJobQueryResult.Refused(DeadLetteredJobQueryOutcome.JobTypeUnknown);
        }

        var query = new DeadLetteredJobQuery(jobType, account, resolvedPageSize, cursor);

        if (cursor is { } presentedCursor
            && !string.Equals(presentedCursor.FilterFingerprint, query.FilterFingerprint, StringComparison.Ordinal))
        {
            return DeadLetteredJobQueryResult.Refused(DeadLetteredJobQueryOutcome.CursorFilterMismatch);
        }

        return DeadLetteredJobQueryResult.Accepted(query);
    }

    /// <summary>Reduces the filters to the short stable text a cursor carries to prove it belongs to this walk.</summary>
    private static string ComputeFingerprint(JobType? jobType, MailAccountIdentity? account) =>
        PageFilterFingerprint.Of(
            jobType?.Name,
            account?.Owner.Value.ToString("N", CultureInfo.InvariantCulture),
            account?.Id.Value);
}

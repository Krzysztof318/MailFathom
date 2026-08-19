// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Operations;

/// <summary>Asks one bounded, keyset-paginated page of what a deployment has been asked to send.</summary>
/// <remarks>
/// <para>
/// Both filters are optional. An outbox belongs to an account rather than to the deployment, but "what is stuck" is
/// asked of the instance first and narrowed afterwards, so the reading is deployment-wide by default; narrowing by
/// stage is what turns it into the two questions an operator actually has — what is still queued, and what nobody can
/// say the outcome of.
/// </para>
/// <para>
/// A page is always bounded and always ordered newest first, so a caller that supplies nothing gets the most recently
/// recorded <see cref="DefaultPageSize" /> sends rather than everything the deployment has ever sent.
/// </para>
/// </remarks>
public sealed record OutboxQuery
{
    /// <summary>The page size a request that names none is served.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>The greatest page size one request may ask for.</summary>
    /// <remarks>
    /// Every field of an entry is itself bounded — an identifier, an account alias, a stage name, counts, instants, and
    /// a coded failure — so this figure bounds what the answer weighs as well as how many rows it names.
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

    private OutboxQuery(
        MailAccountId? accountId,
        OutgoingEmailStage? stage,
        int pageSize,
        OutboxCursor? cursor)
    {
        this.AccountId = accountId;
        this.Stage = stage;
        this.PageSize = pageSize;
        this.Cursor = cursor;
    }

    /// <summary>Gets the account the page is narrowed to, or <see langword="null" /> for every account.</summary>
    public MailAccountId? AccountId { get; }

    /// <summary>Gets the stage the page is narrowed to, or <see langword="null" /> for every stage.</summary>
    public OutgoingEmailStage? Stage { get; }

    /// <summary>Gets how many sends the page holds at most.</summary>
    public int PageSize { get; }

    /// <summary>Gets the boundary a continued walk reads beyond, or <see langword="null" /> for the first page.</summary>
    public OutboxCursor? Cursor { get; }

    /// <summary>Gets the fingerprint of the filters this query reads under, which its cursors are issued against.</summary>
    /// <remarks>
    /// The page size is deliberately not part of it. A caller may ask for a shorter or longer page while continuing the
    /// same walk, and refusing that would be a rule about pacing rather than about which records the boundary sits in.
    /// </remarks>
    public string FilterFingerprint => ComputeFingerprint(this.AccountId, this.Stage);

    /// <summary>Builds a validated query from what a caller asked for, or reports why the request names no page.</summary>
    /// <param name="accountId">The account to narrow to, or <see langword="null" /> for every account.</param>
    /// <param name="stage">The stage to narrow to, or <see langword="null" /> for every stage.</param>
    /// <param name="pageSize">How many sends the page may hold, or <see langword="null" /> for <see cref="DefaultPageSize" />.</param>
    /// <param name="cursor">The boundary a continued walk reads beyond, or <see langword="null" /> for the first page.</param>
    /// <returns>The accepted query, or the refusal naming what the caller has to change.</returns>
    public static OutboxQueryResult Create(
        MailAccountId? accountId,
        OutgoingEmailStage? stage,
        int? pageSize,
        OutboxCursor? cursor)
    {
        var resolvedPageSize = pageSize ?? DefaultPageSize;

        if (resolvedPageSize is < 1 or > MaximumPageSize)
        {
            return OutboxQueryResult.Refused(OutboxQueryOutcome.PageSizeOutOfRange);
        }

        // A stage cast from a number nothing declares would filter on a value no row can carry, which answers with an
        // empty page rather than with the mistake the caller made.
        if (stage is { } named && !Enum.IsDefined(named))
        {
            return OutboxQueryResult.Refused(OutboxQueryOutcome.StageUnknown);
        }

        var query = new OutboxQuery(accountId, stage, resolvedPageSize, cursor);

        if (cursor is { } presentedCursor
            && !string.Equals(presentedCursor.FilterFingerprint, query.FilterFingerprint, StringComparison.Ordinal))
        {
            return OutboxQueryResult.Refused(OutboxQueryOutcome.CursorFilterMismatch);
        }

        return OutboxQueryResult.Accepted(query);
    }

    /// <summary>Reduces the filters to the short stable text a cursor carries to prove it belongs to this walk.</summary>
    private static string ComputeFingerprint(MailAccountId? accountId, OutgoingEmailStage? stage)
    {
        var material = string.Join(
            FingerprintFieldSeparator,
            accountId?.Value ?? string.Empty,
            stage is { } named ? named.ToString() : string.Empty);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..FingerprintLength];
    }

    /// <summary>Names the stages a caller may narrow to, for a refusal that says what to write instead.</summary>
    /// <returns>The declared stage names, separated by commas.</returns>
    public static string DeclaredStages() => string.Join(
        ", ",
        Enum.GetValues<OutgoingEmailStage>().Select(stage => stage.ToString()));
}

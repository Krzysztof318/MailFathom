// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.Domain.Access;
using MailFathom.Domain.Answering.Audit;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves one account's record of the questions this deployment answered from its mailbox.</summary>
/// <remarks>
/// <para>
/// The answer is derived personal data — it says which of a person's messages a question reached and when — so it is
/// served only from the administrative endpoint, only to an authenticated caller, and only for an account the request
/// names. It carries no mail content: an identifier, an endpoint alias, an instruction version, and two bounded outcomes
/// are MailFathom's own names for things, and the messages are named rather than quoted.
/// </para>
/// <para>
/// <strong>The route is published under <c>mailfathom.admin.audit.read</c></strong>, beside the mutation trail and for
/// the same reason: the two together are what an operator answers "why is this message here" and "why did it answer
/// that" from, so one grant provisions and revokes both.
/// </para>
/// <para>
/// The page is bounded and keyset-paginated. A caller walks the record by presenting the cursor the previous page
/// returned, and the walk ends when no cursor comes back — never by comparing a short page against the size that was
/// asked for.
/// </para>
/// </remarks>
internal static class MailAnsweringAuditEndpoint
{
    /// <summary>The route, relative to the administrative prefix the group is mapped beneath.</summary>
    internal const string Route = "/answering/audit";

    /// <summary>Maps the read route into the administrative group, so it inherits that group's authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapMailAnsweringAudit(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(Route, ReadAsync)
            .RequirePermission(MailFathomPermission.AdminAuditRead);
    }

    /// <summary>Serves one page of an account's answering record, or reports what was wrong with the request.</summary>
    /// <param name="account">The configured identifier of the account whose record is read.</param>
    /// <param name="from">The earliest completion instant served, inclusive, or <see langword="null" /> for none.</param>
    /// <param name="before">The completion instant to stop before, exclusive, or <see langword="null" /> for none.</param>
    /// <param name="pageSize">How many entries the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="trail">Reads the page, for a caller the record's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// Every refusal is <c>400</c>, including an account this deployment does not configure. That mirrors the mutation
    /// trail beside it and for the same reason: an unknown account is a mistake in the request the caller wrote rather
    /// than a missing resource, and <c>404</c> is already what a client reads as "this port serves no administrative
    /// endpoint".
    /// </remarks>
    internal static async Task<Results<Ok<MailAnsweringAuditPageResponse>, ProblemHttpResult>> ReadAsync(
        [FromQuery] string? account,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? before,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] IMailAccountCatalog accounts,
        [FromServices] MailAnsweringAuditTrailReader trail,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(trail);

        if (AdminAccountRequest.Resolve(account, accounts) is not { } accountId)
        {
            return AdminAccountRequest.Refuse(account);
        }

        MailAnsweringAuditCursor? decodedCursor = null;

        if (cursor is not null && !MailAnsweringAuditCursor.TryDecode(cursor, out decodedCursor))
        {
            return TypedResults.Problem(
                "The continuation cursor is not one this deployment issued.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var queryResult = MailAnsweringAuditQuery.Create(accountId, from, before, pageSize, decodedCursor);

        if (queryResult.Query is not { } query)
        {
            return TypedResults.Problem(
                DescribeRefusal(queryResult.Outcome),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var page = await trail.ReadPageAsync(query, cancellationToken);

        return TypedResults.Ok(MailAnsweringAuditPageResponse.For(page));
    }

    /// <summary>States what a caller has to change, without restating the filters they already sent.</summary>
    private static string DescribeRefusal(MailAnsweringAuditQueryOutcome outcome) => outcome switch
    {
        MailAnsweringAuditQueryOutcome.PageSizeOutOfRange =>
            $"An answering audit page holds between 1 and {MailAnsweringAuditQuery.MaximumPageSize} entries.",
        MailAnsweringAuditQueryOutcome.TimeRangeEmpty =>
            "The answering audit time range ends at or before it begins, so it names no entries.",
        _ => "The continuation cursor was issued for a different set of answering audit filters.",
    };
}

/// <summary>One page of an account's answering record, as the administrative endpoint serves it.</summary>
/// <param name="Entries">The entries, newest first.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end of the record.</param>
internal sealed record MailAnsweringAuditPageResponse(
    IReadOnlyList<MailAnsweringAuditEntryResponse> Entries,
    string? NextCursor)
{
    /// <summary>Describes one page for the wire.</summary>
    /// <param name="page">The page read from the record.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page" /> is <see langword="null" />.</exception>
    internal static MailAnsweringAuditPageResponse For(MailAnsweringAuditPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new MailAnsweringAuditPageResponse(
            [.. page.Entries.Select(MailAnsweringAuditEntryResponse.For)],
            page.NextCursor?.Encode());
    }
}

/// <summary>One recorded answering run, as the administrative endpoint serves it.</summary>
/// <param name="Id">What addresses this entry.</param>
/// <param name="RunId">The run this entry records, which the entries of the run's other accounts share.</param>
/// <param name="ChatEndpointAlias">This deployment's own configured name for the endpoint the run was conducted through.</param>
/// <param name="InstructionsVersion">The version of the instruction the run was conducted under.</param>
/// <param name="StartedAt">When the run began.</param>
/// <param name="CompletedAt">When the run reached the ending recorded here.</param>
/// <param name="Outcome">How the run ended.</param>
/// <param name="Degradation">The ways the run read less of the mailbox than an undegraded run of the same question would.</param>
/// <param name="Emails">The emails of this account the run retrieved, in the order it first reached each.</param>
internal sealed record MailAnsweringAuditEntryResponse(
    Guid Id,
    Guid RunId,
    string ChatEndpointAlias,
    string InstructionsVersion,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string Outcome,
    string Degradation,
    IReadOnlyList<MailAnsweringAuditedEmailResponse> Emails)
{
    /// <summary>Describes one entry for the wire.</summary>
    /// <param name="entry">The entry read from the record.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry" /> is <see langword="null" />.</exception>
    internal static MailAnsweringAuditEntryResponse For(MailAnsweringAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new MailAnsweringAuditEntryResponse(
            entry.Id.Value,
            entry.RunId.Value,
            entry.ChatEndpointAlias,
            entry.InstructionsVersion,
            entry.StartedAt,
            entry.CompletedAt,
            entry.Outcome.ToString(),
            entry.Degradation.ToString(),
            [.. entry.Emails.Select(MailAnsweringAuditedEmailResponse.For)]);
    }
}

/// <summary>One email an answering run retrieved, as the administrative endpoint serves it.</summary>
/// <param name="Email">The stable local identity, which is the same one every other read names an email by.</param>
/// <param name="Position">Where in what the run retrieved from this account the email was first reached, counted from zero.</param>
/// <param name="WasCited">Whether the published answer named this email as one of its sources.</param>
/// <remarks>
/// The identifier and nothing beside it. Whoever is entitled to read the message fetches it through the reads that
/// already serve one, and a record that repeated its subject here would be a second copy of the mailbox growing under a
/// retention nobody wrote for mail.
/// </remarks>
internal sealed record MailAnsweringAuditedEmailResponse(Guid Email, int Position, bool WasCited)
{
    /// <summary>Describes one retrieved email for the wire.</summary>
    /// <param name="email">The email the entry names.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="email" /> is <see langword="null" />.</exception>
    internal static MailAnsweringAuditedEmailResponse For(MailAnsweringAuditedEmail email)
    {
        ArgumentNullException.ThrowIfNull(email);

        return new MailAnsweringAuditedEmailResponse(email.StoredEmailId.Value, email.Position, email.WasCited);
    }
}

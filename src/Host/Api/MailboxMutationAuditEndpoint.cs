// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Audit;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves one account's record of the changes MailFathom made to its mailbox.</summary>
/// <remarks>
/// <para>
/// The answer is derived personal data — it says where a person's mail has been, when, and at whose instruction — so it
/// is served only from the administrative endpoint, only to an authenticated caller, and only for an account the request
/// names. It carries no mail content: a folder path, a UID, a mutation name, and a requester identity are the server's
/// own or MailFathom's own names for things.
/// </para>
/// <para>
/// <strong>Every authenticated caller may perform every administrative operation.</strong> The endpoint has no
/// permission model, which <see cref="MailboxRefreshTokenEndpoint" /> states in full and which an operator provisions
/// keys against; a credential that can read a session can read this.
/// </para>
/// <para>
/// The page is bounded and keyset-paginated. A caller walks the trail by presenting the cursor the previous page
/// returned, and the walk ends when no cursor comes back — never by comparing a short page against the size that was
/// asked for.
/// </para>
/// </remarks>
internal static class MailboxMutationAuditEndpoint
{
    /// <summary>The route, relative to the administrative prefix the group is mapped beneath.</summary>
    internal const string Route = "/mailbox/mutations/audit";

    /// <summary>Maps the read route into the administrative group, so it inherits that group's authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapMailboxMutationAudit(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(Route, ReadAsync);
    }

    /// <summary>Serves one page of an account's audit trail, or reports what was wrong with the request.</summary>
    /// <param name="account">The configured identifier of the account whose trail is read.</param>
    /// <param name="mutation">The mutation name to narrow to, or <see langword="null" /> for every mutation.</param>
    /// <param name="from">The earliest completion instant served, inclusive, or <see langword="null" /> for none.</param>
    /// <param name="before">The completion instant to stop before, exclusive, or <see langword="null" /> for none.</param>
    /// <param name="pageSize">How many entries the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="store">Reads the page.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// Every refusal is <c>400</c>, including an account this deployment does not configure. That mirrors the write
    /// route beside it and for the same reason: an unknown account is a mistake in the request the caller wrote rather
    /// than a missing resource, and <c>404</c> is already what a client reads as "this port serves no administrative
    /// endpoint".
    /// </remarks>
    internal static async Task<Results<Ok<MailboxMutationAuditPageResponse>, ProblemHttpResult>> ReadAsync(
        [FromQuery] string? account,
        [FromQuery] string? mutation,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? before,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] IMailAccountCatalog accounts,
        [FromServices] IMailboxMutationAuditEntryStore store,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(store);

        if (string.IsNullOrWhiteSpace(account))
        {
            return TypedResults.Problem("The request named no mail account.", statusCode: StatusCodes.Status400BadRequest);
        }

        var accountId = MailAccountId.Create(account);

        if (!accounts.ServedAccounts.Any(account => account.Id == accountId))
        {
            return TypedResults.Problem(
                $"This deployment configures no mail account named '{accountId.Value}'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The unspecified default is what "every mutation" is written as, so an absent filter and a named one reach the
        // query the same way rather than through a second parameter saying whether the first one counts.
        var narrowedMutation = default(MailboxMutation);

        if (mutation is not null && !MailboxMutation.TryParseName(mutation, out narrowedMutation))
        {
            return TypedResults.Problem(
                $"'{mutation}' does not name a change MailFathom makes to a mailbox.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        MailboxMutationAuditCursor? decodedCursor = null;

        if (cursor is not null)
        {
            if (!MailboxMutationAuditCursor.TryDecode(cursor, out var presentedCursor))
            {
                return TypedResults.Problem(
                    "The continuation cursor is not one this deployment issued.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            decodedCursor = presentedCursor;
        }

        var queryResult = MailboxMutationAuditQuery.Create(
            accountId,
            narrowedMutation,
            from,
            before,
            pageSize,
            decodedCursor);

        if (queryResult.Query is not { } query)
        {
            return TypedResults.Problem(
                DescribeRefusal(queryResult.Outcome),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var page = await store.ReadPageAsync(query, cancellationToken);

        return TypedResults.Ok(MailboxMutationAuditPageResponse.For(page));
    }

    /// <summary>States what a caller has to change, without restating the filters they already sent.</summary>
    private static string DescribeRefusal(MailboxMutationAuditQueryOutcome outcome) => outcome switch
    {
        MailboxMutationAuditQueryOutcome.PageSizeOutOfRange =>
            $"An audit trail page holds between 1 and {MailboxMutationAuditQuery.MaximumPageSize} entries.",
        MailboxMutationAuditQueryOutcome.TimeRangeEmpty =>
            "The audit trail time range ends at or before it begins, so it names no entries.",
        _ => "The continuation cursor was issued for a different set of audit trail filters.",
    };
}

/// <summary>One page of an account's audit trail, as the administrative endpoint serves it.</summary>
/// <param name="Entries">The entries, newest first.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end of the trail.</param>
internal sealed record MailboxMutationAuditPageResponse(
    IReadOnlyList<MailboxMutationAuditEntryResponse> Entries,
    string? NextCursor)
{
    /// <summary>Describes one page for the wire.</summary>
    /// <param name="page">The page read from the trail.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page" /> is <see langword="null" />.</exception>
    internal static MailboxMutationAuditPageResponse For(MailboxMutationAuditPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new MailboxMutationAuditPageResponse(
            [.. page.Entries.Select(MailboxMutationAuditEntryResponse.For)],
            page.NextCursor?.Encode());
    }
}

/// <summary>One recorded change to a mailbox, as the administrative endpoint serves it.</summary>
/// <param name="Id">What addresses this entry.</param>
/// <param name="Mutation">The change that was made.</param>
/// <param name="Email">The local email the change was about.</param>
/// <param name="SourceFolder">The remote path of the folder the email was in when the change was asked for.</param>
/// <param name="SourceUidValidity">The UIDVALIDITY that folder reported.</param>
/// <param name="SourceUid">The UID the email carried in that folder.</param>
/// <param name="DestinationFolder">The folder a relocation or a copy named, or <see langword="null" /> for every other change.</param>
/// <param name="PlacementUidValidity">The UIDVALIDITY the destination folder reported, where the server named one.</param>
/// <param name="PlacementUid">The UID the email was assigned there, where the server named one.</param>
/// <param name="DesiredSeenState">Which way a <c>\Seen</c> change was asked for, or <see langword="null" /> for every other change.</param>
/// <param name="RequesterOrigin">What kind of authored act asked.</param>
/// <param name="Requester">The identity that asked, which carries a rule's revision where a rule asked.</param>
/// <param name="RequestedAt">When the change was asked for.</param>
/// <param name="CompletedAt">When the change reached the ending recorded here.</param>
/// <param name="Outcome">How the change ended.</param>
/// <param name="FailureCode">The code an abandoned change was given up on for, or <see langword="null" /> for one that was performed.</param>
internal sealed record MailboxMutationAuditEntryResponse(
    Guid Id,
    string Mutation,
    Guid Email,
    string SourceFolder,
    uint SourceUidValidity,
    uint SourceUid,
    string? DestinationFolder,
    uint? PlacementUidValidity,
    uint? PlacementUid,
    bool? DesiredSeenState,
    string RequesterOrigin,
    string Requester,
    DateTimeOffset RequestedAt,
    DateTimeOffset CompletedAt,
    string Outcome,
    int? FailureCode)
{
    /// <summary>Describes one entry for the wire.</summary>
    /// <param name="entry">The entry read from the trail.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry" /> is <see langword="null" />.</exception>
    internal static MailboxMutationAuditEntryResponse For(MailboxMutationAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new MailboxMutationAuditEntryResponse(
            entry.Id.Value,
            entry.Mutation.Name,
            entry.StoredEmailId.Value,
            entry.SourceFolderPath.Value,
            entry.SourceUidValidity.Value,
            entry.SourceUid.Value,
            entry.DestinationFolderPath?.Value,
            entry.Placement.UidValidity?.Value,
            entry.Placement.Uid?.Value,
            entry.DesiredSeenState,
            entry.Requester.Origin.ToString(),
            entry.Requester.Identity,
            entry.RequestedAt,
            entry.CompletedAt,
            entry.Outcome.ToString(),
            entry.Failure?.Value);
    }
}

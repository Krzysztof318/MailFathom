// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.UserSettings.Administration;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the signed-in user their own record, and takes back the parts of it they maintain.</summary>
/// <remarks>
/// <para>
/// The one place a person changes what this deployment reads for them: which mailboxes it synchronizes, and the
/// settings that belong to those mailboxes. It is the same record an administrator reaches through
/// <see cref="UserRecordEndpoints" />, judged by the same rules and committed by the same writer, so what this surface
/// accepts is exactly what the next start would read.
/// </para>
/// <para>
/// <b>No route here names a user.</b> The acting user comes off the credential that authenticated, which is what
/// makes "a request naming another user" something a caller cannot express rather than something this surface refuses:
/// there is no argument to put another user's identifier in, no listing to discover one from, and no answer whose
/// shape or timing separates a user this deployment does not serve from one it serves and is not you. That is the
/// deployment-wide catalog a user-facing surface must never compose, and the way to be certain of it is to publish no
/// route that could hold one.
/// </para>
/// <para>
/// Reading is <see cref="MailFathomPermission.MailRead" /> — the grant that already carries seeing which accounts this
/// deployment holds for the caller — and every write is <see cref="MailFathomPermission.MailAccountsWrite" />, a grant
/// of its own because it decides which mailboxes the deployment connects to and under whose credentials. A client that
/// reads somebody's mail has not thereby been granted the ability to point this deployment at another mailbox.
/// </para>
/// <para>
/// The record is handed over with every secret-bearing value replaced by the redaction marker, and a save is read as
/// the difference from what the row holds — so a marker saved back leaves the reference beneath it as it was, and one
/// this deployment cannot place is refused rather than committed over the user's own credential.
/// </para>
/// </remarks>
internal static class ClientUserRecordEndpoint
{
    /// <summary>The route the acting user's record is read at and saved back to, relative to the client prefix.</summary>
    internal const string RecordRoute = "/record";

    /// <summary>The route one mail account is declared at.</summary>
    internal const string MailAccountsRoute = $"{RecordRoute}/mail-accounts";

    /// <summary>The route one mail account is withdrawn at.</summary>
    /// <remarks>The identifier travels in the body for the reason <see cref="UserRecordEndpoints.UserMailAccountRemovalRoute" /> gives: it is a name its user chose rather than a generated handle, and a removal that silently addressed nothing is the one outcome this act must not have.</remarks>
    internal const string MailAccountRemovalRoute = $"{MailAccountsRoute}/removal";

    /// <summary>The route one folder of one mail account is declared at.</summary>
    /// <remarks>
    /// Under the record rather than beside <see cref="ClientMailFoldersEndpoint.MailFoldersRoute" />, because making
    /// a folder is a change to what this deployment is configured to read rather than a change to a mailbox. Nothing
    /// here reaches a mail server: the folder becomes a mapping, the account's next run resolves it, and the mapping
    /// is what says the server may be asked to create it. That is the whole reason the two surfaces are apart — one
    /// answers what the folders are, and this one states what they should be.
    /// </remarks>
    internal const string FoldersRoute = $"{MailAccountsRoute}/folders";

    /// <summary>The route one folder is stated afresh at, in place of the one carrying an alias.</summary>
    internal const string FolderReplacementRoute = $"{FoldersRoute}/replacement";

    /// <summary>The route one folder is withdrawn at.</summary>
    /// <remarks>The alias travels in the body for the reason a withdrawn mail account's identifier does, and for one more: an alias nests on a slash, so a name a route segment could carry is not a name every alias has.</remarks>
    internal const string FolderRemovalRoute = $"{FoldersRoute}/removal";

    /// <summary>Maps the record routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientUserRecord(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(RecordRoute, ReadAsync)
            .RequirePermission(MailFathomPermission.MailRead);

        // The attribute is reached for its metadata rather than as an MVC filter: it implements
        // IRequestSizeLimitMetadata, which the routing pipeline applies to the request body feature, so a body over the
        // bound is answered 413 before the handler is reached.
        api.MapPost(RecordRoute, SaveAsync)
            .WithMetadata(new RequestSizeLimitAttribute(UserRecordEndpoints.MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailAccountsWrite);

        api.MapPost(MailAccountsRoute, AddMailAccountAsync)
            .WithMetadata(new RequestSizeLimitAttribute(UserRecordEndpoints.MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailAccountsWrite);

        api.MapPost(MailAccountRemovalRoute, RemoveMailAccountAsync)
            .WithMetadata(new RequestSizeLimitAttribute(UserRecordEndpoints.MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailAccountsWrite);

        api.MapPost(FoldersRoute, AddFolderAsync)
            .WithMetadata(new RequestSizeLimitAttribute(UserRecordEndpoints.MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailAccountsWrite);

        api.MapPost(FolderReplacementRoute, ReplaceFolderAsync)
            .WithMetadata(new RequestSizeLimitAttribute(UserRecordEndpoints.MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailAccountsWrite);

        api.MapPost(FolderRemovalRoute, RemoveFolderAsync)
            .WithMetadata(new RequestSizeLimitAttribute(UserRecordEndpoints.MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailAccountsWrite);
    }

    /// <summary>Hands the acting user their record, as the redacted JSON an editing session opens.</summary>
    /// <param name="records">The record administration.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the record and the version it was read at, <c>404</c> when this deployment holds no record for the caller, or <c>400</c> when the row is not a document of settings.</returns>
    internal static async Task<Results<Ok<UserRecordResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> ReadAsync(
        [FromServices] UserRecordAdministration records,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);

        UserRecordReading? record;

        try
        {
            record = await records.ReadOwnRecordAsync(cancellationToken);
        }
        catch (Exception refusal) when (refusal is FormatException or JsonException)
        {
            // The parser's own message names the offending token and the JSON path it stopped at, and that path is
            // composed from the record's own key names — which here are this person's mailboxes.
            return Refusal(
                "Your record is not a document of settings, so it cannot be read or edited. Ask whoever administers this deployment to correct it.");
        }

        return record is null
            ? NoRecord()
            : TypedResults.Ok(UserRecordResponse.For(record));
    }

    /// <summary>Takes back the record the acting user saved.</summary>
    /// <param name="records">The record administration.</param>
    /// <param name="request">The record and the version the buffer was opened over.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns><c>200</c> with what the write did, <c>404</c> when this deployment holds no record for the caller, or <c>400</c> when the request carries no record.</returns>
    internal static async Task<Results<Ok<UserRecordWriteResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> SaveAsync(
        [FromServices] UserRecordAdministration records,
        [FromBody] UserRecordSaveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(request);

        if (StatedVersion(request.Version) is { } refused)
        {
            return refused;
        }

        if (request.Document is not { Length: > 0 } document)
        {
            return Refusal(
                "A saved record carries the record. An editing session that means to change nothing sends nothing at all.");
        }

        return Answered(await records.ApplyOwnRecordAsync(document, request.Version, cancellationToken));
    }

    /// <summary>Declares one more mail account in the acting user's record.</summary>
    /// <param name="records">The record administration.</param>
    /// <param name="request">The declaration and the version the record was read at.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns><c>200</c> with what the write did, <c>404</c> when this deployment holds no record for the caller, or <c>400</c> when the request carries no declaration.</returns>
    internal static async Task<Results<Ok<UserRecordWriteResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> AddMailAccountAsync(
        [FromServices] UserRecordAdministration records,
        [FromBody] UserMailAccountRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(request);

        if (StatedVersion(request.Version) is { } refused)
        {
            return refused;
        }

        if (request.Account is not { Length: > 0 } account)
        {
            return Refusal("A declared mail account carries the settings the account is read with.");
        }

        return Answered(await records.AddOwnMailAccountAsync(account, request.Version, cancellationToken));
    }

    /// <summary>Withdraws one mail account from the acting user's record.</summary>
    /// <param name="records">The record administration.</param>
    /// <param name="request">The identifier and the version the record was read at.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns><c>200</c> with what the write did, <c>404</c> when this deployment holds no record for the caller, or <c>400</c> when the request names no account.</returns>
    /// <remarks>The mail already stored for that account stays, exactly as it does when a file stops declaring one. Erasing it is a separate act, and it is not this surface's: what this does is stop the deployment reading the mailbox.</remarks>
    internal static async Task<Results<Ok<UserRecordWriteResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> RemoveMailAccountAsync(
        [FromServices] UserRecordAdministration records,
        [FromBody] UserMailAccountRemovalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(request);

        if (StatedVersion(request.Version) is { } refused)
        {
            return refused;
        }

        if (request.AccountId is not { Length: > 0 } accountId || string.IsNullOrWhiteSpace(accountId))
        {
            return Refusal("A withdrawn mail account names the identifier it was declared under.");
        }

        return Answered(await records.RemoveOwnMailAccountAsync(accountId, request.Version, cancellationToken));
    }

    /// <summary>Declares one more folder in one of the acting user's mail accounts.</summary>
    /// <param name="records">The record administration.</param>
    /// <param name="request">The account, the declaration, and the version the record was read at.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns><c>200</c> with what the write did, <c>404</c> when this deployment holds no record for the caller, or <c>400</c> when the request names no account or carries no declaration.</returns>
    internal static async Task<Results<Ok<UserRecordWriteResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> AddFolderAsync(
        [FromServices] UserRecordAdministration records,
        [FromBody] UserFolderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(request);

        if (StatedVersion(request.Version) is { } refused)
        {
            return refused;
        }

        if (Named(request.AccountId) is not { } accountId)
        {
            return Refusal("A declared folder names the mail account it belongs to.");
        }

        if (request.Folder is not { Length: > 0 } folder)
        {
            return Refusal("A declared folder carries the settings the folder is read with.");
        }

        return Answered(await records.AddOwnFolderAsync(accountId, folder, request.Version, cancellationToken));
    }

    /// <summary>States one folder of the acting user's afresh, in place of the one carrying an alias.</summary>
    /// <param name="records">The record administration.</param>
    /// <param name="request">The account, the alias being replaced, the declaration, and the version the record was read at.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns><c>200</c> with what the write did, <c>404</c> when this deployment holds no record for the caller, or <c>400</c> when the request names no account or folder, or carries no declaration.</returns>
    internal static async Task<Results<Ok<UserRecordWriteResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> ReplaceFolderAsync(
        [FromServices] UserRecordAdministration records,
        [FromBody] UserFolderReplacementRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(request);

        if (StatedVersion(request.Version) is { } refused)
        {
            return refused;
        }

        if (Named(request.AccountId) is not { } accountId)
        {
            return Refusal("A replaced folder names the mail account it belongs to.");
        }

        if (Named(request.Alias) is not { } alias)
        {
            return Refusal("A replaced folder names the alias it is declared under now.");
        }

        if (request.Folder is not { Length: > 0 } folder)
        {
            return Refusal("A replaced folder carries the settings it is to stand with.");
        }

        return Answered(await records.ReplaceOwnFolderAsync(accountId, alias, folder, request.Version, cancellationToken));
    }

    /// <summary>Withdraws one folder from one of the acting user's mail accounts.</summary>
    /// <param name="records">The record administration.</param>
    /// <param name="request">The account, the alias, and the version the record was read at.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns><c>200</c> with what the write did, <c>404</c> when this deployment holds no record for the caller, or <c>400</c> when the request names no account or no folder.</returns>
    /// <remarks>The mail already stored out of that folder stays, exactly as it does when a mail account stops being declared. What this does is stop the deployment reading the folder.</remarks>
    internal static async Task<Results<Ok<UserRecordWriteResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> RemoveFolderAsync(
        [FromServices] UserRecordAdministration records,
        [FromBody] UserFolderRemovalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(request);

        if (StatedVersion(request.Version) is { } refused)
        {
            return refused;
        }

        if (Named(request.AccountId) is not { } accountId)
        {
            return Refusal("A withdrawn folder names the mail account it belongs to.");
        }

        if (Named(request.Alias) is not { } alias)
        {
            return Refusal("A withdrawn folder names the alias it was declared under.");
        }

        return Answered(await records.RemoveOwnFolderAsync(accountId, alias, request.Version, cancellationToken));
    }

    /// <summary>Reads a name a request has to carry, or nothing where it carried none.</summary>
    /// <remarks>White space is the case a length check alone lets through, and an alias or an identifier of nothing but spaces reaches the composition as a name that matches no entry — which would be reported as a folder somebody does not have rather than as a request that named none.</remarks>
    private static string? Named(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>Answers what a write did, or that this deployment holds no record for the caller.</summary>
    private static Results<Ok<UserRecordWriteResponse>, NotFound<ProblemDetails>, ProblemHttpResult> Answered(
        UserRecordWriteOutcome? outcome) =>
        outcome is null ? NoRecord() : TypedResults.Ok(UserRecordWriteResponse.For(outcome));

    /// <summary>Says why a stated version is not one this boundary accepts, or nothing where it is.</summary>
    private static ProblemHttpResult? StatedVersion(long version) => version < 0
        ? Refusal("A write to a record states the version it was composed over, which is never negative.")
        : null;

    /// <summary>Answers that this deployment holds no record for the caller.</summary>
    /// <remarks>Reached where the row behind an authenticated caller has gone, which is a user erased under a credential that has not yet been withdrawn.</remarks>
    private static NotFound<ProblemDetails> NoRecord() => TypedResults.NotFound(new ProblemDetails
    {
        Status = StatusCodes.Status404NotFound,
        Detail = "This deployment holds no record for you.",
    });

    private static ProblemHttpResult Refusal(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.OwnerSettings.Administration;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the signed-in owner their own record, and takes back the parts of it they maintain.</summary>
/// <remarks>
/// <para>
/// The one place a person changes what this deployment reads for them: which mailboxes it synchronizes, and the
/// settings that belong to those mailboxes. It is the same record an administrator reaches through
/// <see cref="OwnerRecordEndpoints" />, judged by the same rules and committed by the same writer, so what this surface
/// accepts is exactly what the next start would read.
/// </para>
/// <para>
/// <b>No route here names an owner.</b> The acting owner comes off the credential that authenticated, which is what
/// makes "a request naming another owner" something a caller cannot express rather than something this surface refuses:
/// there is no argument to put another owner's identifier in, no listing to discover one from, and no answer whose
/// shape or timing separates an owner this deployment does not serve from one it serves and is not you. That is the
/// deployment-wide catalog an owner-facing surface must never compose, and the way to be certain of it is to publish no
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
/// this deployment cannot place is refused rather than committed over the owner's own credential.
/// </para>
/// </remarks>
internal static class ClientOwnerRecordEndpoint
{
    /// <summary>The route the acting owner's record is read at and saved back to, relative to the client prefix.</summary>
    internal const string RecordRoute = "/record";

    /// <summary>The route one mail account is declared at.</summary>
    internal const string MailAccountsRoute = $"{RecordRoute}/mail-accounts";

    /// <summary>The route one mail account is withdrawn at.</summary>
    /// <remarks>The identifier travels in the body for the reason <see cref="OwnerRecordEndpoints.OwnerMailAccountRemovalRoute" /> gives: it is a name its owner chose rather than a generated handle, and a removal that silently addressed nothing is the one outcome this act must not have.</remarks>
    internal const string MailAccountRemovalRoute = $"{MailAccountsRoute}/removal";

    /// <summary>Maps the record routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientOwnerRecord(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(RecordRoute, ReadAsync)
            .RequirePermission(MailFathomPermission.MailRead);

        // The attribute is reached for its metadata rather than as an MVC filter: it implements
        // IRequestSizeLimitMetadata, which the routing pipeline applies to the request body feature, so a body over the
        // bound is answered 413 before the handler is reached.
        api.MapPost(RecordRoute, SaveAsync)
            .WithMetadata(new RequestSizeLimitAttribute(OwnerRecordEndpoints.MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailAccountsWrite);

        api.MapPost(MailAccountsRoute, AddMailAccountAsync)
            .WithMetadata(new RequestSizeLimitAttribute(OwnerRecordEndpoints.MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailAccountsWrite);

        api.MapPost(MailAccountRemovalRoute, RemoveMailAccountAsync)
            .WithMetadata(new RequestSizeLimitAttribute(OwnerRecordEndpoints.MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailAccountsWrite);
    }

    /// <summary>Hands the acting owner their record, as the redacted JSON an editing session opens.</summary>
    /// <param name="records">The record administration.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the record and the version it was read at, <c>404</c> when this deployment holds no record for the caller, or <c>400</c> when the row is not a document of settings.</returns>
    internal static async Task<Results<Ok<OwnerRecordResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> ReadAsync(
        [FromServices] OwnerRecordAdministration records,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);

        OwnerRecordReading? record;

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
            : TypedResults.Ok(OwnerRecordResponse.For(record));
    }

    /// <summary>Takes back the record the acting owner saved.</summary>
    /// <param name="records">The record administration.</param>
    /// <param name="request">The record and the version the buffer was opened over.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns><c>200</c> with what the write did, <c>404</c> when this deployment holds no record for the caller, or <c>400</c> when the request carries no record.</returns>
    internal static async Task<Results<Ok<OwnerRecordWriteResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> SaveAsync(
        [FromServices] OwnerRecordAdministration records,
        [FromBody] OwnerRecordSaveRequest request,
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

    /// <summary>Declares one more mail account in the acting owner's record.</summary>
    /// <param name="records">The record administration.</param>
    /// <param name="request">The declaration and the version the record was read at.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns><c>200</c> with what the write did, <c>404</c> when this deployment holds no record for the caller, or <c>400</c> when the request carries no declaration.</returns>
    internal static async Task<Results<Ok<OwnerRecordWriteResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> AddMailAccountAsync(
        [FromServices] OwnerRecordAdministration records,
        [FromBody] OwnerMailAccountRequest request,
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

    /// <summary>Withdraws one mail account from the acting owner's record.</summary>
    /// <param name="records">The record administration.</param>
    /// <param name="request">The identifier and the version the record was read at.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns><c>200</c> with what the write did, <c>404</c> when this deployment holds no record for the caller, or <c>400</c> when the request names no account.</returns>
    /// <remarks>The mail already stored for that account stays, exactly as it does when a file stops declaring one. Erasing it is a separate act, and it is not this surface's: what this does is stop the deployment reading the mailbox.</remarks>
    internal static async Task<Results<Ok<OwnerRecordWriteResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> RemoveMailAccountAsync(
        [FromServices] OwnerRecordAdministration records,
        [FromBody] OwnerMailAccountRemovalRequest request,
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

    /// <summary>Answers what a write did, or that this deployment holds no record for the caller.</summary>
    private static Results<Ok<OwnerRecordWriteResponse>, NotFound<ProblemDetails>, ProblemHttpResult> Answered(
        OwnerRecordWriteOutcome? outcome) =>
        outcome is null ? NoRecord() : TypedResults.Ok(OwnerRecordWriteResponse.For(outcome));

    /// <summary>Says why a stated version is not one this boundary accepts, or nothing where it is.</summary>
    private static ProblemHttpResult? StatedVersion(long version) => version < 0
        ? Refusal("A write to a record states the version it was composed over, which is never negative.")
        : null;

    /// <summary>Answers that this deployment holds no record for the caller.</summary>
    /// <remarks>Reached where the row behind an authenticated caller has gone, which is an owner erased under a credential that has not yet been withdrawn.</remarks>
    private static NotFound<ProblemDetails> NoRecord() => TypedResults.NotFound(new ProblemDetails
    {
        Status = StatusCodes.Status404NotFound,
        Detail = "This deployment holds no record for you.",
    });

    private static ProblemHttpResult Refusal(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
}

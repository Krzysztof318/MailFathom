// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.OwnerSettings.Administration;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Infrastructure.Persistence.Owners;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Secrets.Resolution;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Records the owners this deployment serves, and maintains the record each of them is served from.</summary>
/// <remarks>
/// <para>
/// Two things are administered here and they are deliberately different sizes. The roster is the deployment's — who it
/// holds at all — and every act on it is one call: record somebody, list them, erase somebody. One owner's record is
/// theirs, and the acts on it are the ones an operator performs repeatedly: read it, save it edited, declare one more
/// mailbox, withdraw one, and the adoption that moves them off the deployment's files for good.
/// </para>
/// <para>
/// The whole of it is administrative and none of it is anywhere else. A deployment-wide catalog of the people it serves
/// is the one listing an owner-facing surface must never compose, so it exists here and the client surface publishes
/// the acting owner's own record and nothing beside it. What that costs an administrator is naming the owner in every
/// route, which is also what makes an identifier copied out of the wrong listing answer that no such owner exists
/// rather than editing somebody else's mailboxes.
/// </para>
/// <para>
/// Reading is <see cref="MailFathomPermission.AdminRead" /> and every write but one is
/// <see cref="MailFathomPermission.AdminConfigurationWrite" />, for the reason
/// <see cref="ConfigurationEndpoints" /> gives: a record decides which mailboxes this deployment reads and under whose
/// credentials, which is what the deployment is rather than what it does next. The exception is the erasure, which
/// takes <see cref="MailFathomPermission.AdminErase" /> because it disposes of every message this deployment holds for
/// a person rather than changing what it will read next.
/// </para>
/// <para>
/// No answer here carries a password, a token, or a client secret. A record is handed over with every secret-bearing
/// value replaced by the redaction marker, and a save is read as the difference from what the row holds — so a marker
/// saved back leaves the reference beneath it exactly as it was, and one this deployment cannot place is refused
/// rather than committed over somebody's credential.
/// </para>
/// </remarks>
internal static class OwnerRecordEndpoints
{
    /// <summary>The route the roster is read at and an owner is recorded on, relative to the administrative prefix.</summary>
    internal const string OwnersRoute = "/owners";

    /// <summary>The route one owner is erased at.</summary>
    internal const string OwnerRoute = "/owners/{ownerId:guid}";

    /// <summary>The route one owner's label is replaced at.</summary>
    /// <remarks>Beneath the owner rather than beside the roster, because it changes one owner's row; a route on the collection would read as one that decides which owners there are.</remarks>
    internal const string OwnerDisplayNameRoute = $"{OwnerRoute}/display-name";

    /// <summary>The route one owner's record is read at and saved back to.</summary>
    internal const string OwnerRecordRoute = $"{OwnerRoute}/record";

    /// <summary>The route one mail account is declared at.</summary>
    internal const string OwnerMailAccountsRoute = $"{OwnerRecordRoute}/mail-accounts";

    /// <summary>The route one mail account is withdrawn at.</summary>
    /// <remarks>
    /// The identifier travels in the body rather than in the path, because it is a name an operator chose rather than a
    /// generated handle: a dot, a slash, or a space in one would decide whether the route matched at all, and a removal
    /// that silently addressed nothing is the one outcome this act must not have.
    /// </remarks>
    internal const string OwnerMailAccountRemovalRoute = $"{OwnerMailAccountsRoute}/removal";

    /// <summary>The route one owner's adoption is previewed at and performed on.</summary>
    internal const string OwnerAdoptionRoute = $"{OwnerRecordRoute}/adoption";

    /// <summary>The route material for one owner's record is stored or rotated at.</summary>
    internal const string OwnerSecretsRoute = $"{OwnerRoute}/secrets";

    /// <summary>The greatest request body the write routes read before refusing it.</summary>
    /// <remarks>Twice what an owner's record may be, for the reason <see cref="ConfigurationEndpoints.MaxWriteRequestBytes" /> is twice the deployment document: a body larger than that composes no record this deployment would accept, and the doubling is the room JSON string escaping and the request envelope take on the way.</remarks>
    internal const int MaxWriteRequestBytes = 2 * OwnerSettingsDocument.MaximumOctets;

    /// <summary>The largest encoded request that can contain one bounded secret and its JSON escaping.</summary>
    internal const int MaxStoredSecretWriteRequestBytes = 8 * 1024 * 1024;

    /// <summary>Maps the owner routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapOwnerRecords(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(OwnersRoute, ReadRosterAsync)
            .RequirePermission(MailFathomPermission.AdminRead);

        // The attribute is reached for its metadata rather than as an MVC filter: it implements
        // IRequestSizeLimitMetadata, which the routing pipeline applies to the request body feature, so a body over the
        // bound is answered 413 before the handler is reached.
        api.MapPost(OwnersRoute, ProvisionAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        api.MapDelete(OwnerRoute, EraseAsync)
            .RequirePermission(MailFathomPermission.AdminErase);

        api.MapPut(OwnerDisplayNameRoute, RelabelAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        api.MapGet(OwnerRecordRoute, ReadRecordAsync)
            .RequirePermission(MailFathomPermission.AdminRead);

        api.MapPost(OwnerRecordRoute, SaveRecordAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        api.MapPost(OwnerMailAccountsRoute, AddMailAccountAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        api.MapPost(OwnerMailAccountRemovalRoute, RemoveMailAccountAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        api.MapGet(OwnerAdoptionRoute, ReadAdoptableAsync)
            .RequirePermission(MailFathomPermission.AdminRead);

        api.MapPost(OwnerAdoptionRoute, AdoptAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        api.MapPost(OwnerSecretsRoute, StoreSecretAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxStoredSecretWriteRequestBytes))
            .RequirePermission(MailFathomPermission.AdminConfigurationWrite);
    }

    /// <summary>Lists the owners this deployment holds.</summary>
    /// <param name="roster">The roster administration.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the owners.</returns>
    /// <remarks>An administrator selects an owner before doing anything else here, and this is where the identifier to select comes from; a deployment serving one person answers with one entry, which is what lets a client act without asking.</remarks>
    internal static async Task<Ok<OwnerRosterResponse>> ReadRosterAsync(
        [FromServices] OwnerRosterAdministration roster,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roster);

        return TypedResults.Ok(OwnerRosterResponse.For(await roster.ReadRosterAsync(cancellationToken)));
    }

    /// <summary>Records an owner this deployment did not hold.</summary>
    /// <param name="roster">The roster administration.</param>
    /// <param name="request">The label the owner is told apart by.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the identifier the owner was minted under, or <c>400</c> naming what has to change first.</returns>
    /// <remarks>A refusal is a request the administrator corrects — an endpoint to narrow, a label already taken, a roster at its bound — so it names what to change rather than reporting that something failed.</remarks>
    internal static async Task<Results<Ok<OwnerProvisionedResponse>, ProblemHttpResult>> ProvisionAsync(
        [FromServices] OwnerRosterAdministration roster,
        [FromBody] OwnerProvisioningRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(request);

        var outcome = await roster.ProvisionAsync(request.DisplayName, cancellationToken);

        return outcome.IsProvisioned
            ? TypedResults.Ok(new OwnerProvisionedResponse(outcome.Owner.Value))
            : Refusal(outcome.RefusalMessage!);
    }

    /// <summary>Erases one owner and everything this deployment recorded for them.</summary>
    /// <param name="ownerId">The owner to remove.</param>
    /// <param name="roster">The roster administration.</param>
    /// <param name="cancellationToken">Cancels the erasure before it commits.</param>
    /// <returns><c>200</c> with what was removed, or <c>400</c> when the request names nobody or a configuration source declares the owner.</returns>
    /// <remarks>
    /// An owner this deployment does not hold is reported as nothing erased rather than as a refusal, because the
    /// caller asked for a state and the deployment is in it. That is a claim about the status code and not about the
    /// body: the answer carries whether a row was there, so a caller granted the erasure learns which identifiers this
    /// deployment holds. Nothing here withholds that — the sibling relabel reports the same fact through its own status
    /// code — and nothing needs to, the erasure being the one permission that could act on the answer anyway. An owner
    /// a file declares is the one erasure that is refused instead, because a start writes them back and the refusal
    /// names the declaration to remove first.
    /// </remarks>
    internal static async Task<Results<Ok<OwnerErasureResponse>, ProblemHttpResult>> EraseAsync(
        Guid ownerId,
        [FromServices] OwnerRosterAdministration roster,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roster);

        if (!TryReadOwner(ownerId, out var owner))
        {
            return EmptyOwner();
        }

        var outcome = await roster.EraseAsync(owner, cancellationToken);

        return outcome.RefusalMessage is { } refused
            ? Refusal(refused)
            : TypedResults.Ok(new OwnerErasureResponse(outcome.OwnerErased, outcome.WasServed));
    }

    /// <summary>Replaces the label one owner is told apart by.</summary>
    /// <param name="ownerId">The owner to relabel.</param>
    /// <param name="roster">The roster administration.</param>
    /// <param name="request">The label the owner carries from now on.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> when the row carries the label, <c>404</c> when this deployment holds no such owner, or <c>400</c> naming what has to change first.</returns>
    /// <remarks>
    /// No body comes back, because the label the request carried is the whole of what changed and nothing about the
    /// owner is decided here. An owner this deployment does not hold is the same <c>404</c> the record routes beside
    /// this one answer with, which is why it is that rather than a refusal naming what went wrong: one shape for
    /// "no such owner" across every route addressing one is what makes a client's handling of it one branch. The
    /// status code does report whether this deployment holds the owner, and is not written to withhold it — a
    /// credential holding the configuration write can learn an identifier it already had to name, one identifier per
    /// request, which is the roster's own read only in the sense that guessing a UUID is.
    /// </remarks>
    internal static async Task<Results<NoContent, NotFound<ProblemDetails>, ProblemHttpResult>> RelabelAsync(
        Guid ownerId,
        [FromServices] OwnerRosterAdministration roster,
        [FromBody] OwnerRelabelRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(request);

        if (!TryReadOwner(ownerId, out var owner))
        {
            return EmptyOwner();
        }

        var outcome = await roster.RelabelAsync(owner, request.DisplayName, cancellationToken);

        if (!outcome.OwnerHeld)
        {
            return NoSuchOwner();
        }

        return outcome.RefusalMessage is { } refused
            ? Refusal(refused)
            : TypedResults.NoContent();
    }

    /// <summary>Hands over one owner's record, as the redacted JSON an editing session opens.</summary>
    /// <param name="ownerId">The owner asked about.</param>
    /// <param name="records">The record administration.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the record and the version it was read at, <c>404</c> when this deployment holds no such owner, or <c>400</c> when the row is not a document of settings.</returns>
    internal static async Task<Results<Ok<OwnerRecordResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> ReadRecordAsync(
        Guid ownerId,
        [FromServices] OwnerRecordAdministration records,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (!TryReadOwner(ownerId, out var owner))
        {
            return EmptyOwner();
        }

        OwnerRecordReading? record;

        try
        {
            record = await records.ReadRecordAsync(owner, cancellationToken);
        }
        catch (Exception refusal) when (refusal is FormatException or JsonException)
        {
            // The parser's own message names the offending token, the JSON path it stopped at, and a byte position,
            // and the path is composed from the row's own key names — which for an owner's record are their mailboxes.
            return Refusal(
                "This owner's record is not a document of settings, so it cannot be read or edited. Correct the row where it was written.");
        }

        return record is null
            ? NoSuchOwner()
            : TypedResults.Ok(OwnerRecordResponse.For(record));
    }

    /// <summary>Takes back one owner's record as an editing session saved it.</summary>
    /// <param name="ownerId">The owner whose record is written.</param>
    /// <param name="records">The record administration.</param>
    /// <param name="request">The record and the version the buffer was opened over.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns><c>200</c> with what the write did, <c>404</c> when this deployment holds no such owner, or <c>400</c> when the request carries no record.</returns>
    internal static async Task<Results<Ok<OwnerRecordWriteResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> SaveRecordAsync(
        Guid ownerId,
        [FromServices] OwnerRecordAdministration records,
        [FromBody] OwnerRecordSaveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(request);

        if (!TryReadOwner(ownerId, out var owner))
        {
            return EmptyOwner();
        }

        if (StatedVersion(request.Version) is { } refused)
        {
            return refused;
        }

        if (request.Document is not { Length: > 0 } document)
        {
            return Refusal(
                "A saved owner record carries the record. An editing session that means to change nothing sends nothing at all.");
        }

        return Answered(await records.ApplyRecordAsync(owner, document, request.Version, cancellationToken));
    }

    /// <summary>Declares one more mail account in an owner's record.</summary>
    /// <param name="ownerId">The owner the mailbox belongs to.</param>
    /// <param name="records">The record administration.</param>
    /// <param name="request">The declaration and the version the record was read at.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns><c>200</c> with what the write did, <c>404</c> when this deployment holds no such owner, or <c>400</c> when the request carries no declaration.</returns>
    internal static async Task<Results<Ok<OwnerRecordWriteResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> AddMailAccountAsync(
        Guid ownerId,
        [FromServices] OwnerRecordAdministration records,
        [FromBody] OwnerMailAccountRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(request);

        if (!TryReadOwner(ownerId, out var owner))
        {
            return EmptyOwner();
        }

        if (StatedVersion(request.Version) is { } refused)
        {
            return refused;
        }

        if (request.Account is not { Length: > 0 } account)
        {
            return Refusal("A declared mail account carries the settings the account is read with.");
        }

        return Answered(await records.AddMailAccountAsync(owner, account, request.Version, cancellationToken));
    }

    /// <summary>Withdraws one mail account from an owner's record.</summary>
    /// <param name="ownerId">The owner the mailbox belongs to.</param>
    /// <param name="records">The record administration.</param>
    /// <param name="request">The identifier and the version the record was read at.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns><c>200</c> with what the write did, <c>404</c> when this deployment holds no such owner, or <c>400</c> when the request names no account.</returns>
    /// <remarks>The mail this deployment already stored for that account is deliberately untouched, exactly as it is when a file stops declaring one: erasing it is a separate act somebody means.</remarks>
    internal static async Task<Results<Ok<OwnerRecordWriteResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> RemoveMailAccountAsync(
        Guid ownerId,
        [FromServices] OwnerRecordAdministration records,
        [FromBody] OwnerMailAccountRemovalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(request);

        if (!TryReadOwner(ownerId, out var owner))
        {
            return EmptyOwner();
        }

        if (StatedVersion(request.Version) is { } refused)
        {
            return refused;
        }

        if (request.AccountId is not { Length: > 0 } accountId || string.IsNullOrWhiteSpace(accountId))
        {
            return Refusal("A withdrawn mail account names the identifier it was declared under.");
        }

        return Answered(await records.RemoveMailAccountAsync(owner, accountId, request.Version, cancellationToken));
    }

    /// <summary>Reports what adopting one owner would move out of this deployment's files into their record.</summary>
    /// <param name="ownerId">The owner asked about.</param>
    /// <param name="records">The record administration.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the preview, <c>404</c> when this deployment holds no such owner, or <c>400</c> when the request names nobody.</returns>
    /// <remarks>The preview names the configuration path that stops deciding this owner's mailboxes once the adoption commits, which is the part an operator weighs: the file goes on being read for everybody else and stops being read for them.</remarks>
    internal static async Task<Results<Ok<OwnerAdoptionPreviewResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> ReadAdoptableAsync(
        Guid ownerId,
        [FromServices] OwnerRecordAdministration records,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (!TryReadOwner(ownerId, out var owner))
        {
            return EmptyOwner();
        }

        var preview = await records.ReadAdoptableAsync(owner, cancellationToken);

        return preview is null
            ? NoSuchOwner()
            : TypedResults.Ok(OwnerAdoptionPreviewResponse.For(preview));
    }

    /// <summary>Moves one owner's mail accounts out of this deployment's files and into their own record.</summary>
    /// <param name="ownerId">The owner being adopted.</param>
    /// <param name="records">The record administration.</param>
    /// <param name="request">The version the preview was read over.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns><c>200</c> with what the adoption did, <c>404</c> when this deployment holds no such owner, or <c>400</c> when the request states no version.</returns>
    internal static async Task<Results<Ok<OwnerRecordWriteResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> AdoptAsync(
        Guid ownerId,
        [FromServices] OwnerRecordAdministration records,
        [FromBody] OwnerAdoptionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(request);

        if (!TryReadOwner(ownerId, out var owner))
        {
            return EmptyOwner();
        }

        if (StatedVersion(request.Version) is { } refused)
        {
            return refused;
        }

        return Answered(await records.AdoptAsync(owner, request.Version, cancellationToken));
    }

    /// <summary>Stores or rotates material one owner's record reaches through a database reference.</summary>
    /// <param name="ownerId">The owner whose deletion removes the material.</param>
    /// <param name="request">The stable declared name and material.</param>
    /// <param name="secrets">Performs the bounded sealed write.</param>
    /// <param name="cancellationToken">Cancels the owner read, sealing, or commit.</param>
    /// <returns><c>200</c> with the reference, <c>404</c> when the owner is absent, or <c>400</c> when the request cannot be stored.</returns>
    /// <remarks>
    /// The material reaches this boundary as a string because that is what a JSON body deserializes to. It is copied
    /// immediately into an owned erasable buffer, never echoed, and never included in a refusal; the response carries
    /// only the reference a document keeps.
    /// </remarks>
    internal static async Task<Results<Ok<StoredSecretProvisionedResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> StoreSecretAsync(
        Guid ownerId,
        [FromBody] StoredSecretWriteRequest? request,
        [FromServices] StoredSecretAdministration secrets,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        if (!TryReadOwner(ownerId, out var owner))
        {
            return EmptyOwner();
        }

        if (!SecretName.TryCreate(request?.Name, out var name))
        {
            return Refusal(
                $"The request named no usable secret. Use at most {SecretName.MaximumLength} letters, digits, dots, dashes, or underscores, beginning with a letter or digit.");
        }

        if (request?.Material is not { Length: > 0 })
        {
            return Refusal("The request carried no secret material.");
        }

        StoredSecretProvisioning provisioning;
        try
        {
            using var material = ResolvedSecret.FromText(request.Material);
            provisioning = await secrets.StoreAsync(owner, name, material, cancellationToken);
        }
        catch (ArgumentException)
        {
            return Refusal("The secret material is empty or exceeds the bounded size MailFathom accepts.");
        }

        return provisioning.Outcome switch
        {
            StoredSecretProvisioningOutcome.Stored =>
                TypedResults.Ok(new StoredSecretProvisionedResponse(provisioning.Reference.ConfigurationValue)),
            StoredSecretProvisioningOutcome.UnknownOwner => NoSuchOwner(),
            StoredSecretProvisioningOutcome.KeyRingUnavailable => Refusal(
                "DataEncryption configures no key ring, so this deployment cannot store secret material in the database."),
            _ => Refusal("The stored-secret write was refused for a reason this deployment cannot describe."),
        };
    }

    /// <summary>Answers what a write did, or that this deployment holds no such owner.</summary>
    /// <remarks>Every refusal about the record itself is a success status carrying the outcome, for the reason a configuration write's is: each is something the administrator acts on and continues from, and each carries the version they compose the next attempt over.</remarks>
    private static Results<Ok<OwnerRecordWriteResponse>, NotFound<ProblemDetails>, ProblemHttpResult> Answered(
        OwnerRecordWriteOutcome? outcome) =>
        outcome is null ? NoSuchOwner() : TypedResults.Ok(OwnerRecordWriteResponse.For(outcome));

    /// <summary>Says why a stated version is not one this boundary accepts, or nothing where it is.</summary>
    private static ProblemHttpResult? StatedVersion(long version) => version < 0
        ? Refusal("A write to an owner's record states the version it was composed over, which is never negative.")
        : null;

    /// <summary>Reads the owner a route named, refusing the empty identifier the type will not carry.</summary>
    private static bool TryReadOwner(Guid ownerId, out MailOwnerId owner)
    {
        owner = ownerId == Guid.Empty ? default : MailOwnerId.Create(ownerId);

        return owner.IsSpecified;
    }

    private static ProblemHttpResult EmptyOwner() => Refusal("An owner is named by the identifier this deployment recorded them under.");

    /// <summary>Answers that this deployment holds no such owner.</summary>
    /// <remarks>The same answer an owner this deployment genuinely does not hold receives, which is what keeps a caller from learning which identifiers exist by asking about them.</remarks>
    private static NotFound<ProblemDetails> NoSuchOwner() => TypedResults.NotFound(new ProblemDetails
    {
        Status = StatusCodes.Status404NotFound,
        Detail = "This deployment holds no such owner.",
    });

    private static ProblemHttpResult Refusal(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
}

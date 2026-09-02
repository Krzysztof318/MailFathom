// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves what the signed-in owner does about their own outbox: watch it, and decide about one send in it.</summary>
/// <remarks>
/// <para>
/// It is what a client draws after a message has been sent. Sending queues, so the send the owner just asked for has
/// not left yet, and these routes are where the screen watches it go — and where a send that is still queued is taken
/// back, or one that failed is offered another chance.
/// </para>
/// <para>
/// <b>A listing names an account and never the deployment.</b> The narrowing is required rather than optional, so
/// there is no unnarrowed reading here at all: one that fell back to every account would page through every owner's
/// outgoing mail, which is the deployment-wide catalog an owner-facing surface must never compose. An account another
/// owner owns is refused exactly as one nobody configured, and a send another owner made answers exactly as one
/// nobody made.
/// </para>
/// <para>
/// What the answers may carry is what the administrative outbox already settled and for the same reasons. A page names
/// no recipient and no subject, because a page of an outbox would otherwise be an export of who this owner writes to,
/// a page at a time; one send read by identity names its recipients and what the server told this deployment about
/// each, because that is the question it was asked. Neither reads the message, at any size.
/// </para>
/// <para>
/// Every route here is <c>mailfathom.mail.send</c>, including the two readings. What an outbox says is what this owner
/// is sending, so a credential granted to read a mailbox learns nothing here — and withdrawing a send is part of
/// sending rather than a power beside it.
/// </para>
/// </remarks>
internal static class ClientOutboxEndpoints
{
    /// <summary>The route one page of the owner's outbox is read from, relative to the client prefix.</summary>
    internal const string OutboxRoute = "/outbox";

    /// <summary>The route one of the owner's sends is read from.</summary>
    internal const string OutboxSendRoute = $"{OutboxRoute}/{{outgoingEmailId:guid}}";

    /// <summary>The route one send is withdrawn on.</summary>
    internal const string OutboxCancellationRoute = $"{OutboxRoute}/cancellation";

    /// <summary>The route one send is offered again on.</summary>
    /// <remarks>
    /// A path of its own rather than a field on the cancellation request, for the reason the administrative pair are
    /// two paths: they are opposite decisions, and a body carrying which one was meant would make a mistyped value the
    /// difference between withdrawing a message and sending it a second time.
    /// </remarks>
    internal const string OutboxRequeueRoute = $"{OutboxRoute}/requeue";

    /// <summary>Maps the outbox routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientOutbox(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(OutboxRoute, ReadPageAsync)
            .RequirePermission(MailFathomPermission.MailSend);

        api.MapGet(OutboxSendRoute, ReadSendAsync)
            .RequirePermission(MailFathomPermission.MailSend);

        // The attribute is reached for its metadata rather than as an MVC filter, exactly as the administrative
        // decisions reach it: it implements IRequestSizeLimitMetadata, which the routing pipeline applies to the
        // request body feature, so a body over the bound is answered 413 before the handler is reached.
        api.MapPost(OutboxCancellationRoute, CancelAsync)
            .WithMetadata(new RequestSizeLimitAttribute(OutboxEndpoints.MaxDecisionRequestBytes))
            .RequirePermission(MailFathomPermission.MailSend);

        api.MapPost(OutboxRequeueRoute, RequeueAsync)
            .WithMetadata(new RequestSizeLimitAttribute(OutboxEndpoints.MaxDecisionRequestBytes))
            .RequirePermission(MailFathomPermission.MailSend);
    }

    /// <summary>Serves one page of what the named account of the acting owner is sending, newest first.</summary>
    /// <param name="account">The account to read, by its identifier or its display name, which is required rather than optional.</param>
    /// <param name="stage">The stage to narrow to, or <see langword="null" /> for every stage.</param>
    /// <param name="pageSize">How many sends the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="outbox">Reads the page, for the owner the credential names.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// An account this owner does not own is <c>400</c> rather than <c>404</c>, which is how every other narrowing on
    /// this surface answers one: it is a mistake in the request the client wrote rather than a missing resource, and it
    /// answers identically for an account nobody configured so that nothing here reports whose accounts exist.
    /// </remarks>
    internal static async Task<Results<Ok<OutboxPageResponse>, ProblemHttpResult>> ReadPageAsync(
        [FromQuery] string? account,
        [FromQuery] string? stage,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] OwnerOutbox outbox,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outbox);

        if (NamedAccount(account) is not { } named)
        {
            return Refuse("A reading of an outbox names the account it is reading, which this one named none of.");
        }

        if (!TryReadStage(stage, out var namedStage))
        {
            return Refuse(UnknownStage());
        }

        OutboxCursor? decodedCursor = null;

        if (!string.IsNullOrWhiteSpace(cursor) && !OutboxCursor.TryDecode(cursor, out decodedCursor))
        {
            return Refuse("The continuation cursor is not one this deployment issued.");
        }

        try
        {
            var result = await outbox.ReadPageAsync(
                named,
                namedStage,
                pageSize,
                decodedCursor,
                cancellationToken);

            return result.Page is { } page
                ? TypedResults.Ok(OutboxPageResponse.For(page))
                : Refuse(DescribeRefusal(result.Outcome));
        }
        catch (MailAccountNotAccessibleException)
        {
            return Refuse("The account is not one this owner owns.");
        }
    }

    /// <summary>Serves one of the acting owner's sends, with what each of its recipients was told.</summary>
    /// <param name="outgoingEmailId">The send to read.</param>
    /// <param name="outbox">Reads the record, for the owner the credential names.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the send, or <c>404</c> where this owner has no send under that identifier.</returns>
    /// <remarks>
    /// A send another owner made and one nobody made answer identically. This route addresses one record by identity,
    /// so its absence is the absence of the thing addressed — unlike the two decisions beside it, which address the
    /// outbox and report what became of the send they named.
    /// </remarks>
    internal static async Task<Results<Ok<OutboxSendResponse>, NotFound>> ReadSendAsync(
        [FromRoute] Guid outgoingEmailId,
        [FromServices] OwnerOutbox outbox,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outbox);

        if (NamedSend(outgoingEmailId) is not { } identifier)
        {
            return TypedResults.NotFound();
        }

        return await outbox.FindAsync(identifier, cancellationToken) is { } record
            ? TypedResults.Ok(OutboxSendResponse.For(record))
            : TypedResults.NotFound();
    }

    /// <summary>Withdraws one of the acting owner's sends that has not begun transmitting.</summary>
    /// <param name="request">The send to cancel.</param>
    /// <param name="outbox">Performs the decision, for the owner the credential names.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns><c>200</c> with what happened, or <c>400</c> where the request named no send.</returns>
    /// <remarks>
    /// A send that cannot be withdrawn is an outcome rather than a refusal, and so is one this owner does not hold:
    /// the caller asked a question this deployment can answer, and being told the message has gone past the point of
    /// recall is exactly what somebody acting on a screen a moment old needs.
    /// </remarks>
    internal static async Task<Results<Ok<OutboxDecisionResponse>, ProblemHttpResult>> CancelAsync(
        [FromBody] OutboxCancellationRequest? request,
        [FromServices] OwnerOutbox outbox,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outbox);

        if (NamedSend(request?.OutgoingEmail) is not { } identifier)
        {
            return Refuse(NoSendNamed());
        }

        var outcome = await outbox.CancelAsync(identifier, cancellationToken);

        return TypedResults.Ok(OutboxDecisionResponse.For(identifier, outcome));
    }

    /// <summary>Offers one of the acting owner's sends again, which is the decision this system will not take on its own.</summary>
    /// <param name="request">The send to offer again, and whether a permanent refusal was restated.</param>
    /// <param name="outbox">Performs the decision, for the owner the credential names.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns><c>200</c> with what happened, or <c>400</c> where the request named no send.</returns>
    /// <remarks>
    /// It names one send and never a set, for the reason the administrative decision does: a message whose outcome
    /// nobody knows may already be in somebody's mailbox, so a filtered re-queue would be an unknown number of
    /// duplicates asked for in one request.
    /// </remarks>
    internal static async Task<Results<Ok<OutboxDecisionResponse>, ProblemHttpResult>> RequeueAsync(
        [FromBody] OutboxRequeueRequest? request,
        [FromServices] OwnerOutbox outbox,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outbox);

        if (NamedSend(request?.OutgoingEmail) is not { } identifier)
        {
            return Refuse(NoSendNamed());
        }

        var outcome = await outbox.RequeueAsync(
            identifier,
            request?.RefusalRestated ?? false,
            cancellationToken);

        return TypedResults.Ok(OutboxDecisionResponse.For(identifier, outcome));
    }

    /// <summary>Reads the account a request narrows to, refusing text no name of this system is spelled with.</summary>
    private static MailAccountSelector? NamedAccount(string? account)
    {
        if (string.IsNullOrWhiteSpace(account))
        {
            return null;
        }

        try
        {
            return MailAccountSelector.Create(account);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Reads the stage a request narrows to, taking every stage where it names none.</summary>
    private static bool TryReadStage(string? stage, out OutgoingEmailStage? named)
    {
        named = null;

        if (string.IsNullOrWhiteSpace(stage))
        {
            return true;
        }

        if (!Enum.TryParse<OutgoingEmailStage>(stage, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            return false;
        }

        named = parsed;

        return true;
    }

    /// <summary>Reads the send a request named, keeping an absent identifier apart from an unusable one.</summary>
    private static OutgoingEmailId? NamedSend(Guid? outgoingEmail) =>
        outgoingEmail is { } identifier && identifier != Guid.Empty
            ? OutgoingEmailId.Create(identifier)
            : null;

    /// <summary>States what a caller has to change, without restating the filters they already sent.</summary>
    private static string DescribeRefusal(OutboxQueryOutcome outcome) => outcome switch
    {
        OutboxQueryOutcome.PageSizeOutOfRange =>
            $"A page of the outbox holds between 1 and {OutboxQuery.MaximumPageSize} records.",
        OutboxQueryOutcome.StageUnknown => UnknownStage(),
        _ => "The continuation cursor was issued for a different set of outbox filters.",
    };

    /// <summary>States that the request named no stage a send can stand at.</summary>
    private static string UnknownStage() =>
        $"The stage filter names no stage a queued message stands at. It is one of {OutboxQuery.DeclaredStages()}.";

    /// <summary>States that the request named no send to decide about.</summary>
    private static string NoSendNamed() =>
        "The request named no queued message. Name the identifier the outbox reading reports for it.";

    /// <summary>States what a caller has to change, without echoing what they sent.</summary>
    private static ProblemHttpResult Refuse(string stated) =>
        TypedResults.Problem(stated, statusCode: StatusCodes.Status400BadRequest);
}

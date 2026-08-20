// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves what an operator does about an outbox: see what is in it, and decide about one send that is stuck.</summary>
/// <remarks>
/// <para>
/// Five routes, and they are two readings and two decisions. The summary answers whether mail is leaving at all; the
/// listing answers what is queued and what failed; the single-record reading answers who one message was for and what
/// each of them was told; and the two decisions are the only points at which sending is reversible and the only point
/// at which a send nothing will attempt again is offered another chance.
/// </para>
/// <para>
/// They are here rather than on the MCP surface because none of them is anything a model reasons over, and because
/// putting a message back on its way to somebody's mailbox should be bounded by the credential that bounds everything
/// else administrative. The summary and the listing are <c>mailfathom.admin.read</c>, so a monitoring credential can
/// watch an outbox it cannot act on; the single-record reading is <c>mailfathom.admin.audit.read</c>, because it is the
/// one of the five that names people; and both decisions are <c>mailfathom.admin.operate</c>.
/// </para>
/// <para>
/// What the answers may carry differs by route, deliberately. The summary and the listing name no recipient and no
/// subject, because a page of an outbox would otherwise be an export of who this owner writes to, a page at a time. The
/// single-record reading names its recipients and what the server said about each, because it was asked about one send
/// by identity and cannot answer without them. None of the five reads the message: no subject, no body, and no raw MIME
/// is loaded on this surface at all.
/// </para>
/// </remarks>
internal static class OutboxEndpoints
{
    /// <summary>The route one page of the recorded sends is read from, relative to the administrative prefix.</summary>
    internal const string OutboxRoute = "/outbox";

    /// <summary>The route the counts by stage are read from, relative to the administrative prefix.</summary>
    /// <remarks>A literal segment where the single-send route takes an identifier, which a deployment's routing prefers over a parameter, so the two cannot be confused.</remarks>
    internal const string SummaryRoute = "/outbox/summary";

    /// <summary>The route one recorded send is read from, relative to the administrative prefix.</summary>
    internal const string SendRoute = "/outbox/{outgoingEmailId:guid}";

    /// <summary>The route one send is withdrawn on, relative to the administrative prefix.</summary>
    internal const string CancellationRoute = "/outbox/cancellation";

    /// <summary>The route one send is offered again on, relative to the administrative prefix.</summary>
    /// <remarks>
    /// A path of its own rather than a field on the cancellation request, because the two are opposite decisions and a
    /// body that carried which one was meant would make a mistyped value the difference between withdrawing a message
    /// and sending it a second time.
    /// </remarks>
    internal const string RequeueRoute = "/outbox/requeue";

    /// <summary>The greatest request body either decision route reads before refusing it.</summary>
    /// <remarks>
    /// The body names one send and whether a refusal was restated, so a few hundred bytes is the whole of anything it
    /// could mean. Stated for the reason every other administrative write states it: the server's own default is
    /// measured in tens of megabytes, which here would let an authenticated client make the process buffer a body four
    /// orders of magnitude larger than the request it is sending.
    /// </remarks>
    internal const int MaxDecisionRequestBytes = 4 * 1024;

    /// <summary>Maps the outbox routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapOutbox(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(SummaryRoute, ReadSummaryAsync)
            .RequirePermission(MailFathomPermission.AdminRead);

        api.MapGet(OutboxRoute, ReadPageAsync)
            .RequirePermission(MailFathomPermission.AdminRead);

        // The one reading that names people, and therefore the one published under the grant every other reading of
        // identified third parties is published under rather than under the grant its two neighbours share.
        api.MapGet(SendRoute, ReadSendAsync)
            .RequirePermission(MailFathomPermission.AdminAuditRead);

        // The attribute is reached for its metadata rather than as an MVC filter, exactly as the dead-letter decisions
        // reach it: it implements IRequestSizeLimitMetadata, which the routing pipeline applies to the request body
        // feature, so a body over the bound is answered 413 before the handler is reached.
        api.MapPost(CancellationRoute, CancelAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxDecisionRequestBytes))
            .RequirePermission(MailFathomPermission.AdminOperate);

        api.MapPost(RequeueRoute, RequeueAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxDecisionRequestBytes))
            .RequirePermission(MailFathomPermission.AdminOperate);
    }

    /// <summary>Serves how much stands at each stage of an outbox.</summary>
    /// <param name="account">The configured identifier of the account to narrow to, or <see langword="null" /> for every account.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="outbox">Reads the counts.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the counts, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<Ok<OutboxSummaryResponse>, ProblemHttpResult>> ReadSummaryAsync(
        [FromQuery] string? account,
        [FromServices] IMailAccountCatalog accounts,
        [FromServices] OutboxOperations outbox,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(outbox);

        if (!AdminAccountRequest.TryResolveFilter(account, accounts, out var accountId, out var refusal))
        {
            return refusal;
        }

        var summary = await outbox.ReadSummaryAsync(accountId, cancellationToken);

        return TypedResults.Ok(OutboxSummaryResponse.For(summary));
    }

    /// <summary>Serves one page of the sends this deployment has recorded, newest first.</summary>
    /// <param name="account">The configured identifier of the account to narrow to, or <see langword="null" /> for every account.</param>
    /// <param name="stage">The stage to narrow to, or <see langword="null" /> for every stage.</param>
    /// <param name="pageSize">How many sends the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="outbox">Reads the page.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// Every refusal is <c>400</c>, including an account this deployment does not configure, which mirrors the other
    /// paged administrative readings: an unknown account is a mistake in the request the caller wrote rather than a
    /// missing resource, and <c>404</c> is already what a client reads as "this port serves no administrative endpoint".
    /// </remarks>
    internal static async Task<Results<Ok<OutboxPageResponse>, ProblemHttpResult>> ReadPageAsync(
        [FromQuery] string? account,
        [FromQuery] string? stage,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] IMailAccountCatalog accounts,
        [FromServices] OutboxOperations outbox,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(outbox);

        if (!AdminAccountRequest.TryResolveFilter(account, accounts, out var accountId, out var refusal))
        {
            return refusal;
        }

        OutgoingEmailStage? namedStage = null;

        if (stage is not null)
        {
            if (!Enum.TryParse<OutgoingEmailStage>(stage, ignoreCase: true, out var parsedStage)
                || !Enum.IsDefined(parsedStage))
            {
                return UnknownStage();
            }

            namedStage = parsedStage;
        }

        OutboxCursor? decodedCursor = null;

        if (cursor is not null && !OutboxCursor.TryDecode(cursor, out decodedCursor))
        {
            return TypedResults.Problem(
                "The continuation cursor is not one this deployment issued.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var queryResult = OutboxQuery.Create(accountId, namedStage, pageSize, decodedCursor);

        if (queryResult.Query is not { } query)
        {
            return TypedResults.Problem(
                DescribeRefusal(queryResult.Outcome),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var page = await outbox.ReadPageAsync(query, cancellationToken);

        return TypedResults.Ok(OutboxPageResponse.For(page));
    }

    /// <summary>Serves one recorded send, with what each of its recipients was told.</summary>
    /// <param name="outgoingEmailId">The send to read.</param>
    /// <param name="outbox">Reads the record.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the send, <c>404</c> when this deployment holds none with that identifier, or <c>400</c> when the request named none.</returns>
    /// <remarks>
    /// A send this deployment does not hold is <c>404</c> here rather than an outcome in a <c>200</c>, unlike the two
    /// decisions beside it. This route addresses one record by identity, so its absence is the absence of the thing
    /// addressed; a decision addresses the outbox and reports what became of the send it named, which is a question this
    /// deployment can answer either way.
    /// </remarks>
    internal static async Task<Results<Ok<OutboxSendResponse>, NotFound, ProblemHttpResult>> ReadSendAsync(
        Guid outgoingEmailId,
        [FromServices] OutboxOperations outbox,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outbox);

        if (ResolveSend(outgoingEmailId) is not { } identifier)
        {
            return NoSendNamed();
        }

        var record = await outbox.FindAsync(identifier, cancellationToken);

        return record is null ? TypedResults.NotFound() : TypedResults.Ok(OutboxSendResponse.For(record));
    }

    /// <summary>Withdraws one send that has not begun transmitting.</summary>
    /// <param name="request">The send to cancel.</param>
    /// <param name="outbox">Performs the decision.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with what happened, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// A send that cannot be withdrawn is an outcome rather than a refusal, so it is answered <c>200</c> with that
    /// outcome named: the caller asked a question this deployment can answer, and the answer is that the message has
    /// gone past the point of recall — which is exactly what an operator acting on a listing a moment old needs to be
    /// told, rather than a status they have to interpret.
    /// </remarks>
    internal static async Task<Results<Ok<OutboxDecisionResponse>, ProblemHttpResult>> CancelAsync(
        [FromBody] OutboxCancellationRequest? request,
        [FromServices] OutboxOperations outbox,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outbox);

        if (ResolveSend(request?.OutgoingEmail) is not { } identifier)
        {
            return NoSendNamed();
        }

        var outcome = await outbox.CancelAsync(identifier, cancellationToken);

        return TypedResults.Ok(OutboxDecisionResponse.For(identifier, outcome));
    }

    /// <summary>Offers one send again, which is the decision this system deliberately will not take on its own.</summary>
    /// <param name="request">The send to offer again, and whether a permanent refusal was restated.</param>
    /// <param name="outbox">Performs the decision.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with what happened, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// It names one send and never a set. A message whose outcome nobody knows may already be in somebody's mailbox, so
    /// offering it again is a decision about that one message; a route that could re-queue a filtered selection would be
    /// a way to send an unknown number of duplicates with one request.
    /// </remarks>
    internal static async Task<Results<Ok<OutboxDecisionResponse>, ProblemHttpResult>> RequeueAsync(
        [FromBody] OutboxRequeueRequest? request,
        [FromServices] OutboxOperations outbox,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outbox);

        if (ResolveSend(request?.OutgoingEmail) is not { } identifier)
        {
            return NoSendNamed();
        }

        var outcome = await outbox.RequeueAsync(
            identifier,
            request?.RefusalRestated ?? false,
            cancellationToken);

        return TypedResults.Ok(OutboxDecisionResponse.For(identifier, outcome));
    }

    /// <summary>Reads the send a request named, keeping an absent identifier apart from an unusable one.</summary>
    private static OutgoingEmailId? ResolveSend(Guid? outgoingEmail) =>
        outgoingEmail is { } identifier && identifier != Guid.Empty
            ? OutgoingEmailId.Create(identifier)
            : null;

    /// <summary>States that the request named no stage a send can stand at.</summary>
    private static ProblemHttpResult UnknownStage() => TypedResults.Problem(
        $"The stage filter names no stage a queued message stands at. It is one of {OutboxQuery.DeclaredStages()}.",
        statusCode: StatusCodes.Status400BadRequest);

    /// <summary>States that the request named no send to decide about.</summary>
    private static ProblemHttpResult NoSendNamed() => TypedResults.Problem(
        "The request named no queued message. Name the identifier the outbox reading reports for it.",
        statusCode: StatusCodes.Status400BadRequest);

    /// <summary>States what a caller has to change, without restating the filters they already sent.</summary>
    private static string DescribeRefusal(OutboxQueryOutcome outcome) => outcome switch
    {
        OutboxQueryOutcome.PageSizeOutOfRange =>
            $"A page of the outbox holds between 1 and {OutboxQuery.MaximumPageSize} records.",
        OutboxQueryOutcome.StageUnknown =>
            $"The stage filter names no stage a queued message stands at. It is one of {OutboxQuery.DeclaredStages()}.",
        _ => "The continuation cursor was issued for a different set of outbox filters.",
    };
}

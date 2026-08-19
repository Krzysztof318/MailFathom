// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
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

        MailAccountId? accountId = null;

        if (account is not null)
        {
            if (ResolveAccount(account, accounts) is not { } servedAccount)
            {
                return UnknownAccount(account);
            }

            accountId = servedAccount;
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

        MailAccountId? accountId = null;

        if (account is not null)
        {
            if (ResolveAccount(account, accounts) is not { } servedAccount)
            {
                return UnknownAccount(account);
            }

            accountId = servedAccount;
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

        if (cursor is not null)
        {
            if (!OutboxCursor.TryDecode(cursor, out var presentedCursor))
            {
                return TypedResults.Problem(
                    "The continuation cursor is not one this deployment issued.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            decodedCursor = presentedCursor;
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

    /// <summary>Reads the account a request named, or nothing when this deployment does not serve it.</summary>
    private static MailAccountId? ResolveAccount(string? account, IMailAccountCatalog accounts)
    {
        if (string.IsNullOrWhiteSpace(account))
        {
            return null;
        }

        var accountId = MailAccountId.Create(account);

        return accounts.ServedAccounts.Any(served => served.Id == accountId) ? accountId : null;
    }

    /// <summary>States that the request named no account this deployment serves, without echoing an empty one.</summary>
    private static ProblemHttpResult UnknownAccount(string? account) => TypedResults.Problem(
        string.IsNullOrWhiteSpace(account)
            ? "The account filter named no mail account. Leave it out to read every account."
            : $"This deployment configures no mail account named '{account}'.",
        statusCode: StatusCodes.Status400BadRequest);

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

/// <summary>What a deployment is asked when one send is to be withdrawn.</summary>
/// <param name="OutgoingEmail">The identifier the outbox reading reports for the send.</param>
internal sealed record OutboxCancellationRequest(Guid? OutgoingEmail);

/// <summary>What a deployment is asked when one send is to be offered again.</summary>
/// <param name="OutgoingEmail">The identifier the outbox reading reports for the send.</param>
/// <param name="RefusalRestated">Whether the caller has restated a permanent refusal, which is what a refused send needs before it is offered again.</param>
internal sealed record OutboxRequeueRequest(Guid? OutgoingEmail, bool RefusalRestated);

/// <summary>How much stands at each stage of an outbox.</summary>
/// <param name="Stages">One count per declared stage, in the order the stages are declared.</param>
/// <param name="OutstandingCount">How many sends nothing has finished with, which is the depth an operator means.</param>
internal sealed record OutboxSummaryResponse(IReadOnlyList<OutboxStageCountResponse> Stages, int OutstandingCount)
{
    /// <summary>Describes the summary as the administrative surface reports it.</summary>
    /// <param name="summary">The summary read.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="summary" /> is <see langword="null" />.</exception>
    internal static OutboxSummaryResponse For(OutboxSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new OutboxSummaryResponse(
            [.. summary.Stages.Select(stage => new OutboxStageCountResponse(stage.Stage.ToString(), stage.Count))],
            summary.OutstandingCount);
    }
}

/// <summary>How many sends stand at one stage.</summary>
/// <param name="Stage">The stage.</param>
/// <param name="Count">How many sends stand at it.</param>
internal sealed record OutboxStageCountResponse(string Stage, int Count);

/// <summary>One page of what a deployment has been asked to send.</summary>
/// <param name="Sends">The sends, ordered by when each one was written down, newest first.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end.</param>
internal sealed record OutboxPageResponse(IReadOnlyList<OutboxEntryResponse> Sends, string? NextCursor)
{
    /// <summary>Describes one page as the administrative surface reports it.</summary>
    /// <param name="page">The page read.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page" /> is <see langword="null" />.</exception>
    internal static OutboxPageResponse For(OutboxPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new OutboxPageResponse(
            [.. page.Sends.Select(OutboxEntryResponse.For)],
            page.NextCursor?.Encode());
    }
}

/// <summary>One recorded send as a listing names it.</summary>
/// <param name="OutgoingEmail">The identifier a decision names it by.</param>
/// <param name="Account">The account the message is sent from.</param>
/// <param name="Stage">How far along its submission sequence it has durably reached.</param>
/// <param name="Origin">What asked for the send.</param>
/// <param name="AttemptCount">How many attempts have been handed out for it.</param>
/// <param name="MimeByteLength">How many bytes of MIME are stored for the message.</param>
/// <param name="RecordedAt">When the send was written down.</param>
/// <param name="StageChangedAt">When it last moved between stages.</param>
/// <param name="AvailableAt">The instant from which it may be claimed again.</param>
/// <param name="LastFailureCode">The code identifying what the last attempt ended in, absent where the row records none.</param>
/// <param name="LastReplyCode">The reply code the server answered with, absent where it answered none.</param>
internal sealed record OutboxEntryResponse(
    Guid OutgoingEmail,
    string Account,
    string Stage,
    string Origin,
    int AttemptCount,
    long MimeByteLength,
    DateTimeOffset RecordedAt,
    DateTimeOffset StageChangedAt,
    DateTimeOffset AvailableAt,
    int? LastFailureCode,
    int? LastReplyCode)
{
    /// <summary>Describes one listing entry as the administrative surface reports it.</summary>
    /// <param name="entry">The entry read.</param>
    /// <returns>The response record.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry" /> is <see langword="null" />.</exception>
    internal static OutboxEntryResponse For(OutboxEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new OutboxEntryResponse(
            entry.OutgoingEmailId.Value,
            entry.AccountId.Value,
            entry.Stage.ToString(),
            entry.Origin.ToString(),
            entry.AttemptCount,
            entry.MimeByteLength,
            entry.RecordedAt,
            entry.StageChangedAt,
            entry.AvailableAt,
            entry.LastFailure?.Value,
            entry.LastReplyCode);
    }
}

/// <summary>One recorded send, with what each of its recipients was told.</summary>
/// <param name="OutgoingEmail">The identifier a decision names it by.</param>
/// <param name="Account">The account the message is sent from.</param>
/// <param name="Stage">How far along its submission sequence it has durably reached.</param>
/// <param name="Origin">What asked for the send.</param>
/// <param name="Requester">The identity the send is idempotent under, which is MailFathom's own name for what asked.</param>
/// <param name="AttemptCount">How many attempts have been handed out for it.</param>
/// <param name="MimeByteLength">How many bytes of MIME are stored for the message.</param>
/// <param name="RecordedAt">When the send was written down.</param>
/// <param name="StageChangedAt">When it last moved between stages.</param>
/// <param name="AvailableAt">The instant from which it may be claimed again.</param>
/// <param name="LastFailureCode">The code identifying what the last attempt ended in, absent where the row records none.</param>
/// <param name="LastReplyCode">The reply code the server answered with, absent where it answered none.</param>
/// <param name="Recipients">Who the message is offered to, and what each of them was told.</param>
internal sealed record OutboxSendResponse(
    Guid OutgoingEmail,
    string Account,
    string Stage,
    string Origin,
    string Requester,
    int AttemptCount,
    long MimeByteLength,
    DateTimeOffset RecordedAt,
    DateTimeOffset StageChangedAt,
    DateTimeOffset AvailableAt,
    int? LastFailureCode,
    int? LastReplyCode,
    IReadOnlyList<OutboxRecipientResponse> Recipients)
{
    /// <summary>Describes one send as the administrative surface reports it.</summary>
    /// <param name="record">The record read.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record" /> is <see langword="null" />.</exception>
    internal static OutboxSendResponse For(OutgoingEmailRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new OutboxSendResponse(
            record.Id.Value,
            record.AccountId.Value,
            record.Stage.ToString(),
            record.Requester.Origin.ToString(),
            record.Requester.Identity,
            record.AttemptCount,
            record.MimeByteLength,
            record.RecordedAt,
            record.StageChangedAt,
            record.AvailableAt,
            record.LastFailure?.Value,
            record.LastReplyCode,
            [.. record.Recipients.Select(OutboxRecipientResponse.For)]);
    }
}

/// <summary>One person a message is offered to, and what the server said about them.</summary>
/// <param name="Address">The address the envelope names.</param>
/// <param name="Role">Whether the address is on the message as a recipient, a copy, or a blind copy.</param>
/// <param name="Status">What the last attempt settled about it.</param>
/// <param name="LastReplyCode">The reply code the server answered for this address, absent where it answered none.</param>
/// <param name="AnsweredAt">When that answer was recorded, absent where none was.</param>
internal sealed record OutboxRecipientResponse(
    string Address,
    string Role,
    string Status,
    int? LastReplyCode,
    DateTimeOffset? AnsweredAt)
{
    /// <summary>Describes one recipient as the administrative surface reports it.</summary>
    /// <param name="outcome">The outcome read.</param>
    /// <returns>The response record.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="outcome" /> is <see langword="null" />.</exception>
    internal static OutboxRecipientResponse For(OutgoingRecipientOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new OutboxRecipientResponse(
            outcome.Recipient.Address.Address,
            outcome.Recipient.Role.ToString(),
            outcome.Status.ToString(),
            outcome.LastReplyCode,
            outcome.AnsweredAt);
    }
}

/// <summary>What became of a send an operator decided about.</summary>
/// <param name="OutgoingEmail">The send the decision named.</param>
/// <param name="Outcome">What happened: <c>Accepted</c>, <c>RecordUnknown</c>, <c>StageDoesNotAllowIt</c>, <c>AttemptUnderWay</c>, or <c>RefusalNotRestated</c>.</param>
internal sealed record OutboxDecisionResponse(Guid OutgoingEmail, string Outcome)
{
    /// <summary>Describes one decision as the administrative surface reports it.</summary>
    /// <param name="outgoingEmailId">The send the decision named.</param>
    /// <param name="outcome">What happened.</param>
    /// <returns>The response body.</returns>
    internal static OutboxDecisionResponse For(OutgoingEmailId outgoingEmailId, OutboxDecisionOutcome outcome) =>
        new(outgoingEmailId.Value, outcome.ToString());
}

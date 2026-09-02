// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations.Authoring;
using MailFathom.Application.Mail.Mutations.Authoring.Failures;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Takes the changes a person makes to their own mailbox from the client, and reports where each one got to.</summary>
/// <remarks>
/// <para>
/// Nothing here reaches a mail server. Every route writes a durable record per change and answers with it, and the
/// account's own convergence pass issues the IMAP command later — which is
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>'s
/// arrangement rather than a second one. So a screen never waits on IMAP, a crash between the record and the command
/// leaves a change that converges rather than a mailbox that quietly disagrees, and an account nobody can connect to
/// produces pending changes rather than failures.
/// </para>
/// <para>
/// <b>The changes travel in batches because a screen does.</b> Somebody triaging mail flags four messages at once and
/// expects the list to move at once, so one request carries the lot. Each message is written down on its own, in its
/// own commit: a batch is not one transaction, because one message that has since been deleted must not withdraw the
/// three that were fine. What is atomic is one message's own values, which is where a partial answer would actually
/// hurt — a caller told its call failed while one of three flags was already on its way has no way to find out which.
/// </para>
/// <para>
/// <b>Flags and tags are separate parts of the request, because they are different things with different
/// consequences.</b> A flag is IMAP's own — <c>\Seen</c> and <c>\Flagged</c> are the two ADR 0007 permits — and lives
/// on the mail server. A tag is a keyword, which also lives on the mail server and which the owner sees as a label in
/// their own mail client. MailFathom holds no tag of its own today, so no tag this surface accepts stays local: every
/// one of them is written to the mailbox, and the request says so by naming the mailbox as where both parts land.
/// </para>
/// <para>
/// <b>Moving is its own route with its own grant.</b> A wrong flag misdescribes mail the owner can still find; a wrong
/// move puts it somewhere else, and on a server without <c>MOVE</c> the sequence is a copy and a delete whose failure
/// between them is the one mailbox change that can lose mail. <see cref="MailFathomPermission.MailMove" /> is therefore
/// granted apart from <see cref="MailFathomPermission.MailFlagsWrite" />, and the half-finished case is reported rather
/// than guessed at: a placement whose acknowledgement never arrived is answered as an unknown outcome that a person
/// resolves, never as a change still quietly converging.
/// </para>
/// <para>
/// Nothing on this surface sets <c>\Seen</c> as a consequence of anything. Marking a message read is a change a caller
/// asks for here like any other, which is what
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0026-marking-a-message-read-when-a-person-opens-it-in-the-client.md">ADR 0026</see>
/// settled; the routes that serve mail hold no write session and cannot reach one.
/// </para>
/// <para>
/// No route names an owner, and none carries mail content in either direction. What travels is a local message
/// identity the caller already holds, MailFathom's own name for a folder, a mutation name, a lifecycle name, a count,
/// and an error code — so nothing here reaches a log or a span that could not already be read from configuration.
/// </para>
/// </remarks>
internal static class ClientMailMutationsEndpoint
{
    /// <summary>The route where the changes a caller authored are read back, relative to the client prefix.</summary>
    internal const string MutationsRoute = "/mutations";

    /// <summary>The route flag and tag changes are submitted at.</summary>
    internal const string FlagMutationsRoute = $"{MutationsRoute}/flags";

    /// <summary>The route flag and tag changes are withdrawn at.</summary>
    internal const string FlagWithdrawalsRoute = $"{FlagMutationsRoute}/withdrawals";

    /// <summary>The route folder moves are submitted at.</summary>
    internal const string MoveMutationsRoute = $"{MutationsRoute}/moves";

    /// <summary>The route folder moves are withdrawn at.</summary>
    internal const string MoveWithdrawalsRoute = $"{MoveMutationsRoute}/withdrawals";

    /// <summary>The greatest number of messages, moves, or withdrawals one request may carry in its body.</summary>
    /// <remarks>
    /// It is the bound the reading use case applies as well, so a withdrawal can take back what one submission
    /// authored. A selection running past it is submitted in several requests, which costs the client a loop and keeps
    /// the answer to any one request a size this deployment chose rather than the caller.
    /// </remarks>
    internal const int MaximumChangesPerRequest = MailboxChangeProgressReader.MaximumRecordsPerRead;

    /// <summary>The greatest number of records one read may name.</summary>
    /// <remarks>
    /// Smaller than the bound above because this one travels in the request line rather than in a body, and Kestrel
    /// refuses a request line over 8192 bytes before any handler sees it: at forty-four bytes per <c>record=</c>
    /// parameter, a read the surface published as acceptable would be answered <c>414</c> instead. This number leaves
    /// most of that budget spare, so a reverse proxy that lengthens the path does not turn a documented read into a
    /// refused one. A caller holding more records than this reads them in pages, which it is already doing — one
    /// submitted batch opens a record per value asked for rather than per message, so no single number could have made
    /// one read cover one submission anyway.
    /// </remarks>
    internal const int MaximumChangesPerRead = 100;

    /// <summary>The greatest size a submitted batch may have on the wire.</summary>
    /// <remarks>
    /// Generous against the bound above — a message identity, two flags, and a short keyword list per entry — and small
    /// enough that a body is refused before it is read rather than after. The count is what actually bounds the work;
    /// this bounds what has to be buffered to find out the count.
    /// </remarks>
    internal const int MaxWriteRequestBytes = 256 * 1024;

    /// <summary>Maps the mutation routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientMailMutations(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(MutationsRoute, ReadChangesAsync)
            .RequirePermission(MailFathomPermission.MailRead);

        // The attribute is reached for its metadata rather than as an MVC filter: it implements
        // IRequestSizeLimitMetadata, which the routing pipeline applies to the request body feature, so a body over the
        // bound is answered 413 before the handler is reached.
        api.MapPost(FlagMutationsRoute, SubmitFlagChangesAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailFlagsWrite);

        api.MapPost(FlagWithdrawalsRoute, WithdrawFlagChangesAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailFlagsWrite);

        api.MapPost(MoveMutationsRoute, SubmitMovesAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailMove);

        // Withdrawing a move is admitted under the grant that authored one rather than under a grant of its own,
        // because withdrawal can only stop a mailbox change and never cause one. That is also why it is a route beside
        // the moves instead of one withdrawal route for the whole surface: a route carries one name, and the caller
        // that may stop a move is the caller that could have asked for it.
        api.MapPost(MoveWithdrawalsRoute, WithdrawMovesAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailMove);
    }

    /// <summary>Reports where each of the named changes stands.</summary>
    /// <param name="records">The records to ask about, repeated once per record.</param>
    /// <param name="progress">Reads the records this caller holds.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with one entry per record this caller holds, <c>400</c> when more than <see cref="MaximumChangesPerRead" /> are named, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.read</c>.</returns>
    /// <remarks>A record this caller does not hold is absent from the answer rather than reported as missing, so asking about an identity says nothing about whether it exists.</remarks>
    internal static async Task<Results<Ok<ClientMailChangesResponse>, ProblemHttpResult>> ReadChangesAsync(
        [FromQuery(Name = "record")] Guid[] records,
        [FromServices] MailboxChangeProgressReader progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        if (records is not { Length: > 0 })
        {
            return Refusal("A read of authored changes names at least one record.");
        }

        if (records.Length > MaximumChangesPerRead)
        {
            return TooManyRead();
        }

        var read = await progress.ReadAsync(
            [.. records.Select(MailboxMutationRecordId.Create)],
            cancellationToken);

        return TypedResults.Ok(ClientMailChangesResponse.For(read));
    }

    /// <summary>Writes down the flag and tag changes one batch asks for, one message at a time.</summary>
    /// <param name="request">The messages to change and what to change about each.</param>
    /// <param name="recorder">Writes one message's changes down.</param>
    /// <param name="cancellationToken">Cancels the writes when the client disconnects.</param>
    /// <returns><c>200</c> with one result per message, <c>400</c> when the batch itself is not one this boundary accepts, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.flags.write</c>.</returns>
    /// <remarks>
    /// A refusal about one message is that message's result rather than the request's, which is what lets a batch over
    /// mail that has moved on since the screen drew it report exactly which entries did not apply. The refusals are
    /// caught here because this is where the caller acts on them and carries on; every other failure travels.
    /// </remarks>
    internal static async Task<Results<Ok<ClientMailFlagChangesResponse>, ProblemHttpResult>> SubmitFlagChangesAsync(
        [FromBody] ClientMailFlagChangesRequest? request,
        [FromServices] MailFlagChangeRecorder recorder,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        if (request?.Changes is not { Count: > 0 } changes)
        {
            return Refusal("A batch of flag changes names at least one message.");
        }

        if (changes.Count > MaximumChangesPerRequest)
        {
            return TooMany();
        }

        var requester = RequesterOf(request.RequestId);

        if (requester is null)
        {
            return UnusableRequestId();
        }

        var results = new List<ClientMailFlagChangeResultResponse>(changes.Count);

        foreach (var change in changes)
        {
            results.Add(await RecordFlagChangeAsync(recorder, change, requester, cancellationToken));
        }

        return TypedResults.Ok(new ClientMailFlagChangesResponse(results));
    }

    /// <summary>Writes down the moves one batch asks for, one message at a time.</summary>
    /// <param name="request">The messages to move and where each is going.</param>
    /// <param name="recorder">Writes one move down.</param>
    /// <param name="cancellationToken">Cancels the resolutions and the writes when the client disconnects.</param>
    /// <returns><c>200</c> with one result per message, <c>400</c> when the batch itself is not one this boundary accepts, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.move</c>.</returns>
    /// <remarks>Every per-message refusal is already a result rather than a failure, so nothing is caught here: the use case reports a message that has gone, a folder that is not there, and a message already filed as the three separate answers a person needs.</remarks>
    internal static async Task<Results<Ok<ClientMailMovesResponse>, ProblemHttpResult>> SubmitMovesAsync(
        [FromBody] ClientMailMovesRequest? request,
        [FromServices] MailRelocationRecorder recorder,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        if (request?.Moves is not { Count: > 0 } moves)
        {
            return Refusal("A batch of moves names at least one message.");
        }

        if (moves.Count > MaximumChangesPerRequest)
        {
            return TooMany();
        }

        var requester = RequesterOf(request.RequestId);

        if (requester is null)
        {
            return UnusableRequestId();
        }

        var results = new List<ClientMailMoveResultResponse>(moves.Count);

        foreach (var move in moves)
        {
            results.Add(await RecordMoveAsync(recorder, move, requester, cancellationToken));
        }

        return TypedResults.Ok(new ClientMailMovesResponse(results));
    }

    /// <summary>Withdraws flag and tag changes that have not reached the mail server.</summary>
    /// <param name="request">The records to withdraw.</param>
    /// <param name="withdrawer">Withdraws them.</param>
    /// <param name="cancellationToken">Cancels the read and the commit when the client disconnects.</param>
    /// <returns><c>200</c> with each record as it now stands, <c>400</c> when the batch itself is not one this boundary accepts, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.flags.write</c>.</returns>
    internal static Task<Results<Ok<ClientMailChangesResponse>, ProblemHttpResult>> WithdrawFlagChangesAsync(
        [FromBody] ClientMailChangeWithdrawalRequest? request,
        [FromServices] MailboxChangeWithdrawer withdrawer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(withdrawer);

        return WithdrawAsync(
            request,
            recordIds => withdrawer.WithdrawFlagChangesAsync(recordIds, cancellationToken));
    }

    /// <summary>Withdraws moves that have not reached the mail server.</summary>
    /// <param name="request">The records to withdraw.</param>
    /// <param name="withdrawer">Withdraws them.</param>
    /// <param name="cancellationToken">Cancels the read and the commit when the client disconnects.</param>
    /// <returns><c>200</c> with each record as it now stands, <c>400</c> when the batch itself is not one this boundary accepts, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.move</c>.</returns>
    internal static Task<Results<Ok<ClientMailChangesResponse>, ProblemHttpResult>> WithdrawMovesAsync(
        [FromBody] ClientMailChangeWithdrawalRequest? request,
        [FromServices] MailboxChangeWithdrawer withdrawer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(withdrawer);

        return WithdrawAsync(
            request,
            recordIds => withdrawer.WithdrawMovesAsync(recordIds, cancellationToken));
    }

    /// <summary>Reads one withdrawal batch and hands it to whichever half of the surface the route belongs to.</summary>
    /// <remarks>The two routes differ in the grant they carry and in the changes they cover, and in nothing this method does, so the checks are stated once rather than twice.</remarks>
    private static async Task<Results<Ok<ClientMailChangesResponse>, ProblemHttpResult>> WithdrawAsync(
        ClientMailChangeWithdrawalRequest? request,
        Func<IReadOnlyList<MailboxMutationRecordId>, Task<IReadOnlyList<MailboxChangeProgress>>> withdraw)
    {
        if (request?.RecordIds is not { Count: > 0 } recordIds)
        {
            return Refusal("A withdrawal names at least one record.");
        }

        if (recordIds.Count > MaximumChangesPerRequest)
        {
            return TooMany();
        }

        var withdrawn = await withdraw([.. recordIds.Select(MailboxMutationRecordId.Create)]);

        return TypedResults.Ok(ClientMailChangesResponse.For(withdrawn));
    }

    /// <summary>Writes one message's flag and tag changes down, reporting a refusal about it as that message's result.</summary>
    private static async Task<ClientMailFlagChangeResultResponse> RecordFlagChangeAsync(
        MailFlagChangeRecorder recorder,
        ClientMailFlagChangeRequest? change,
        MailboxMutationRequester requester,
        CancellationToken cancellationToken)
    {
        if (change is null || change.StoredEmailId == Guid.Empty)
        {
            return ClientMailFlagChangeResultResponse.NotRecorded(
                change?.StoredEmailId ?? Guid.Empty,
                ClientMailChangeOutcomes.MessageNotFound,
                detail: null);
        }

        try
        {
            var authored = AuthoredMailFlagChange.Create(
                StoredEmailId.Create(change.StoredEmailId),
                change.Flags?.Seen,
                change.Flags?.Flagged,
                KeywordDirectionOf(change.Tags),
                change.Tags?.Keywords);

            var recorded = await recorder.RecordAsync(authored, requester, cancellationToken);

            return ClientMailFlagChangeResultResponse.Recorded(recorded);
        }
        catch (AuthoredMailChangeTargetNotFoundException)
        {
            return ClientMailFlagChangeResultResponse.NotRecorded(
                change.StoredEmailId,
                ClientMailChangeOutcomes.MessageNotFound,
                detail: null);
        }
        catch (MailFlagChangeInvalidException refusal)
        {
            // The message is relayed because it is written for somebody to read and carries no mail content — the type
            // takes its text as an operator-safe sentence rather than assembling one from what the caller sent.
            return ClientMailFlagChangeResultResponse.NotRecorded(
                change.StoredEmailId,
                ClientMailChangeOutcomes.ChangeNotUsable,
                refusal.Message);
        }
    }

    /// <summary>Writes one move down, reporting the use case's own answer as that message's result.</summary>
    private static async Task<ClientMailMoveResultResponse> RecordMoveAsync(
        MailRelocationRecorder recorder,
        ClientMailMoveRequest? move,
        MailboxMutationRequester requester,
        CancellationToken cancellationToken)
    {
        if (move is null || move.StoredEmailId == Guid.Empty)
        {
            return ClientMailMoveResultResponse.NotRecorded(
                move?.StoredEmailId ?? Guid.Empty,
                MailRelocationOutcome.MessageNotFound);
        }

        if (!MailFolderAlias.TryCreate(move.DestinationFolder, out var destination))
        {
            return ClientMailMoveResultResponse.NotRecorded(
                move.StoredEmailId,
                MailRelocationOutcome.DestinationNotFound);
        }

        var result = await recorder.RecordAsync(
            StoredEmailId.Create(move.StoredEmailId),
            destination,
            requester,
            cancellationToken);

        return ClientMailMoveResultResponse.For(move.StoredEmailId, result);
    }

    /// <summary>Names the invocation asking, from what the caller supplied or from an identity of MailFathom's own.</summary>
    /// <returns>The requester, or <see langword="null" /> where the caller supplied text no record could be written under.</returns>
    /// <remarks>
    /// One identity covers the whole batch, because what a repeated request has to be recognized by is the occurrence,
    /// the requester, and the mutation together — and the occurrences in one batch are all different. A caller that sent
    /// nothing gets a fresh identity, which is the honest reading of a request that declined to say whether it was a
    /// retry: two such calls are two requests, and collapsing them would silently discard the second of a star and an
    /// unstar.
    /// </remarks>
    private static MailboxMutationRequester? RequesterOf(string? requestId)
    {
        if (requestId is null)
        {
            return MailboxMutationRequester.Command(Guid.CreateVersion7().ToString());
        }

        if (string.IsNullOrWhiteSpace(requestId)
            || requestId.Length > MailboxMutationRequester.MaximumIdentityLength
            || requestId.Any(char.IsControl))
        {
            return null;
        }

        return MailboxMutationRequester.Command(requestId);
    }

    /// <summary>Reads the keyword direction the wire value names.</summary>
    /// <exception cref="MailFlagChangeInvalidException">Thrown when the tag change names no direction this surface publishes, which the caller meets as that message's own refusal.</exception>
    private static MailKeywordChangeDirection? KeywordDirectionOf(ClientMailTagChangeRequest? tags) => tags?.Change switch
    {
        null => null,
        ClientMailTagChanges.Add => MailKeywordChangeDirection.Add,
        ClientMailTagChanges.Remove => MailKeywordChangeDirection.Remove,
        ClientMailTagChanges.Replace => MailKeywordChangeDirection.Replace,
        _ => throw MailFlagChangeInvalidException.UnknownKeywordDirection(),
    };

    /// <summary>Answers that the batch names more changes than one request may carry.</summary>
    private static ProblemHttpResult TooMany() =>
        Refusal($"One request carries at most {MaximumChangesPerRequest} changes.");

    /// <summary>Answers that the read names more records than one request line may carry.</summary>
    private static ProblemHttpResult TooManyRead() =>
        Refusal($"One read names at most {MaximumChangesPerRead} records.");

    /// <summary>Answers that the identity the caller stated is not one a record can be written under.</summary>
    private static ProblemHttpResult UnusableRequestId() => Refusal(
        "A stated request identifier is at most "
        + $"{MailboxMutationRequester.MaximumIdentityLength} characters and carries no control character.");

    private static ProblemHttpResult Refusal(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>The names this surface reports an attempted change by.</summary>
/// <remarks>
/// <para>
/// It is the vocabulary both submitting routes answer in, so a client renders one set of outcomes rather than one per
/// route. Which of them a route can produce differs, because a folder is only named by a move.
/// </para>
/// <para>
/// They are published names rather than an enumeration serialized by position, for the reason every other name this
/// system publishes is: the word is the identity, it is what a client branches on and what somebody reads in a support
/// conversation, and an ordinal would change meaning silently if the set were reordered.
/// </para>
/// </remarks>
internal static class ClientMailChangeOutcomes
{
    /// <summary>The change was written down, and the account's next convergence pass will issue it.</summary>
    internal const string Recorded = "recorded";

    /// <summary>This deployment serves no readable message under that identity, so there was nothing to change.</summary>
    internal const string MessageNotFound = "message-not-found";

    /// <summary>The change itself is not one a mail server could be asked for, and the reason is stated beside it.</summary>
    internal const string ChangeNotUsable = "change-not-usable";

    /// <summary>The named destination is not a folder of the account this caller may file into.</summary>
    internal const string DestinationNotFound = "destination-not-found";

    /// <summary>The message is already in the destination folder, so nothing was written down.</summary>
    internal const string AlreadyInDestination = "already-in-destination";

    /// <summary>The destination is a folder this deployment does not mirror, and the account no longer declares what it keeps of mail that leaves it.</summary>
    internal const string AccountNoLongerConfigured = "account-no-longer-configured";
}

/// <summary>The names this surface accepts for what a caller wants done with the tags it listed.</summary>
/// <remarks>
/// Text rather than an enumeration, because an enumeration would arrive over JSON as a number and a client would then
/// be binding to declaration order. The direction is stated rather than inferred from the list, because an empty list
/// means nothing to do for two of the three and <em>carry no tag at all</em> for the third.
/// </remarks>
internal static class ClientMailTagChanges
{
    /// <summary>Put the listed tags on the message, leaving every other tag it carries alone.</summary>
    internal const string Add = "add";

    /// <summary>Take the listed tags off the message, leaving every tag it was not asked about alone.</summary>
    internal const string Remove = "remove";

    /// <summary>Make the message's tags exactly the listed ones, which for an empty list clears every tag it has.</summary>
    internal const string Replace = "replace";
}

/// <summary>One batch of flag and tag changes, each naming the message it is about.</summary>
/// <param name="RequestId">The caller's own identity for this request, or <see langword="null" /> to have one generated.</param>
/// <param name="Changes">The messages to change, at most the batch bound this surface publishes.</param>
/// <remarks>The identity makes a retried request the same request: the batch is answered from the records the first call opened rather than opening a second set. A new value, or none, is a new request, which is what lets somebody star a message, unstar it, and star it again.</remarks>
internal sealed record ClientMailFlagChangesRequest(
    string? RequestId,
    IReadOnlyList<ClientMailFlagChangeRequest> Changes);

/// <summary>The changes one message is asked for, with the mail server's flags and its tags stated apart.</summary>
/// <param name="StoredEmailId">The message to change, as a list row, a conversation, or a search published it.</param>
/// <param name="Flags">Where to leave the flags the mail server keeps, or <see langword="null" /> to leave both alone.</param>
/// <param name="Tags">What to do with the message's tags, or <see langword="null" /> to leave them alone.</param>
/// <remarks>
/// The two parts are separate because they are different acts on the mail server with different consequences: a flag
/// is one bit IMAP defines, and a tag is a keyword the owner sees as a label. Both reach the mailbox, and a change
/// naming neither is refused rather than reported as a change of nothing.
/// </remarks>
internal sealed record ClientMailFlagChangeRequest(
    Guid StoredEmailId,
    ClientMailFlagStateRequest? Flags,
    ClientMailTagChangeRequest? Tags);

/// <summary>Where a change leaves the two flags the mail server keeps for a message.</summary>
/// <param name="Seen"><see langword="true" /> marks the message read, <see langword="false" /> marks it unread, and <see langword="null" /> leaves the flag where it stands.</param>
/// <param name="Flagged"><see langword="true" /> stars the message, <see langword="false" /> unstars it, and <see langword="null" /> leaves the flag where it stands.</param>
/// <remarks>These are the two flags ADR 0007 permits to be written. Reading mail through MailFathom never sets <c>\Seen</c>, so a change asked for here is the only way it moves.</remarks>
internal sealed record ClientMailFlagStateRequest(bool? Seen, bool? Flagged);

/// <summary>What a change does to a message's tags, which are the keywords its mail server stores.</summary>
/// <param name="Change">What to do with the listed tags, named by one of the words this surface publishes.</param>
/// <param name="Keywords">The tags the change names, as the caller wrote them, which are the keywords the mail server stores.</param>
/// <remarks>Both halves are required together: a list without a direction and a direction without a list are each half a change, and guessing which was meant would make clearing every tag unreachable and an accidental empty list destructive.</remarks>
internal sealed record ClientMailTagChangeRequest(string? Change, IReadOnlyList<string>? Keywords);

/// <summary>What one batch of flag and tag changes produced, one result per message it named.</summary>
/// <param name="Results">One entry per message, in the order the batch stated them.</param>
internal sealed record ClientMailFlagChangesResponse(IReadOnlyList<ClientMailFlagChangeResultResponse> Results);

/// <summary>What asking to change one message's flags and tags produced.</summary>
/// <param name="StoredEmailId">The message the entry answers for, as the request named it.</param>
/// <param name="Outcome">What happened, as the outcome's own name.</param>
/// <param name="Detail">Why the change itself was not usable, and <see langword="null" /> for every other outcome.</param>
/// <param name="Changes">One durable record per value that was written down, and empty where none was.</param>
/// <remarks>A record per value rather than per message, because that is the unit convergence resumes, abandons, and attributes an observation back to — so a message whose <c>\Seen</c> change completes while its tags are still converging reports exactly that.</remarks>
internal sealed record ClientMailFlagChangeResultResponse(
    Guid StoredEmailId,
    string Outcome,
    string? Detail,
    IReadOnlyList<ClientMailRecordedChangeResponse> Changes)
{
    /// <summary>Describes a message whose changes were written down.</summary>
    /// <param name="recorded">What the use case answered.</param>
    /// <returns>The response entry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="recorded" /> is <see langword="null" />.</exception>
    internal static ClientMailFlagChangeResultResponse Recorded(AuthoredMailFlagChangeResult recorded)
    {
        ArgumentNullException.ThrowIfNull(recorded);

        return new ClientMailFlagChangeResultResponse(
            recorded.StoredEmailId.Value,
            ClientMailChangeOutcomes.Recorded,
            Detail: null,
            [.. recorded.Recorded.Select(ClientMailRecordedChangeResponse.For)]);
    }

    /// <summary>Describes a message whose changes were not written down, and why.</summary>
    /// <param name="storedEmailId">The message the entry answers for.</param>
    /// <param name="outcome">The published name of the reason nothing was written down.</param>
    /// <param name="detail">The operator-safe sentence saying why, where the outcome carries one.</param>
    /// <returns>The response entry.</returns>
    internal static ClientMailFlagChangeResultResponse NotRecorded(
        Guid storedEmailId,
        string outcome,
        string? detail) => new(storedEmailId, outcome, detail, []);
}

/// <summary>One batch of folder moves, each naming the message it is about and where it is going.</summary>
/// <param name="RequestId">The caller's own identity for this request, or <see langword="null" /> to have one generated.</param>
/// <param name="Moves">The messages to move, at most the batch bound this surface publishes.</param>
internal sealed record ClientMailMovesRequest(string? RequestId, IReadOnlyList<ClientMailMoveRequest> Moves);

/// <summary>One message to move, and the folder it is going to.</summary>
/// <param name="StoredEmailId">The message to move, as a list row, a conversation, or a search published it.</param>
/// <param name="DestinationFolder">MailFathom's own name for the folder, exactly as the folders route publishes it.</param>
/// <remarks>The folder is named by its alias rather than by its place on the server, because an alias keeps its meaning when the server renames or recreates the folder behind it, and because it is what the folders route already gave the client.</remarks>
internal sealed record ClientMailMoveRequest(Guid StoredEmailId, string DestinationFolder);

/// <summary>What one batch of moves produced, one result per message it named.</summary>
/// <param name="Results">One entry per message, in the order the batch stated them.</param>
internal sealed record ClientMailMovesResponse(IReadOnlyList<ClientMailMoveResultResponse> Results);

/// <summary>What asking to move one message produced.</summary>
/// <param name="StoredEmailId">The message the entry answers for, as the request named it.</param>
/// <param name="Outcome">What happened, as the outcome's own name.</param>
/// <param name="DestinationFolder">The folder the move was recorded against, and <see langword="null" /> where none was.</param>
/// <param name="Change">The durable record the move is carried by, and <see langword="null" /> where none was opened.</param>
internal sealed record ClientMailMoveResultResponse(
    Guid StoredEmailId,
    string Outcome,
    string? DestinationFolder,
    ClientMailRecordedChangeResponse? Change)
{
    /// <summary>Describes what the use case answered about one move.</summary>
    /// <param name="storedEmailId">The message the entry answers for.</param>
    /// <param name="result">What the use case answered.</param>
    /// <returns>The response entry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
    internal static ClientMailMoveResultResponse For(Guid storedEmailId, AuthoredMailRelocationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ClientMailMoveResultResponse(
            storedEmailId,
            OutcomeOf(result.Outcome),
            result.Destination?.Value,
            result is { RecordId: { } recordId, Lifecycle: { } lifecycle }
                ? new ClientMailRecordedChangeResponse(
                    MailboxMutation.Relocate.Name,
                    recordId.Value,
                    lifecycle.Name)
                : null);
    }

    /// <summary>Describes a move the boundary refused before the use case was reached.</summary>
    /// <param name="storedEmailId">The message the entry answers for.</param>
    /// <param name="outcome">The reason nothing was written down.</param>
    /// <returns>The response entry.</returns>
    internal static ClientMailMoveResultResponse NotRecorded(Guid storedEmailId, MailRelocationOutcome outcome) =>
        new(storedEmailId, OutcomeOf(outcome), DestinationFolder: null, Change: null);

    /// <summary>Reads the published outcome the use case's own answer names.</summary>
    private static string OutcomeOf(MailRelocationOutcome outcome) => outcome switch
    {
        MailRelocationOutcome.Recorded => ClientMailChangeOutcomes.Recorded,
        MailRelocationOutcome.MessageNotFound => ClientMailChangeOutcomes.MessageNotFound,
        MailRelocationOutcome.DestinationNotFound => ClientMailChangeOutcomes.DestinationNotFound,
        MailRelocationOutcome.AlreadyInDestination => ClientMailChangeOutcomes.AlreadyInDestination,
        MailRelocationOutcome.AccountNoLongerConfigured => ClientMailChangeOutcomes.AccountNoLongerConfigured,
        _ => throw new ArgumentOutOfRangeException(
            nameof(outcome),
            outcome,
            "A move outcome is one this surface publishes."),
    };
}

/// <summary>One durable record a submitted change was written down as.</summary>
/// <param name="Mutation">The change that was written down, under the name every log line and counter uses for it.</param>
/// <param name="RecordId">The record everything afterwards refers to that change by.</param>
/// <param name="State">Where that record stands, which is pending for a change nothing has attempted yet.</param>
/// <remarks>The state is reported rather than assumed to be pending, because a request repeated under an identity that already produced a record is answered with that record and the stage it has since reached.</remarks>
internal sealed record ClientMailRecordedChangeResponse(string Mutation, Guid RecordId, string State)
{
    /// <summary>Describes one record a flag or tag change opened.</summary>
    /// <param name="recorded">The record the use case reported.</param>
    /// <returns>The response entry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="recorded" /> is <see langword="null" />.</exception>
    internal static ClientMailRecordedChangeResponse For(RecordedMailFlagMutation recorded)
    {
        ArgumentNullException.ThrowIfNull(recorded);

        return new ClientMailRecordedChangeResponse(
            recorded.Mutation.Name,
            recorded.RecordId.Value,
            recorded.Lifecycle.Name);
    }
}

/// <summary>The records a withdrawal names.</summary>
/// <param name="RecordIds">The records to withdraw, at most the batch bound this surface publishes.</param>
internal sealed record ClientMailChangeWithdrawalRequest(IReadOnlyList<Guid> RecordIds);

/// <summary>Where each of a caller's own changes stands.</summary>
/// <param name="Changes">One entry per record this caller holds, oldest first, and empty where it holds none of them.</param>
internal sealed record ClientMailChangesResponse(IReadOnlyList<ClientMailChangeStateResponse> Changes)
{
    /// <summary>Describes what the use case read.</summary>
    /// <param name="changes">The progress the use case reported.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="changes" /> is <see langword="null" />.</exception>
    internal static ClientMailChangesResponse For(IReadOnlyList<MailboxChangeProgress> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return new ClientMailChangesResponse([.. changes.Select(ClientMailChangeStateResponse.For)]);
    }
}

/// <summary>Where one change has got to, as the client that authored it reads.</summary>
/// <param name="RecordId">The record this answers for.</param>
/// <param name="StoredEmailId">The message the change is about.</param>
/// <param name="Mutation">The change that was asked for.</param>
/// <param name="State">Whether it is waiting, on its way, done, stuck, or withdrawn.</param>
/// <param name="OutcomeUnknown">Whether a command that may never be issued twice went out and its answer never came back.</param>
/// <param name="AttemptCount">How many times it has been attempted, which is what says a retried change is making no progress.</param>
/// <param name="LastFailure">The code identifying what the last attempt ended in, or <see langword="null" /> while none has failed.</param>
/// <param name="RecordedAt">When the change was written down.</param>
/// <param name="StateChangedAt">When it last moved, which is what says how long a stuck change has been stuck.</param>
/// <remarks>
/// The unknown outcome is its own field rather than a state, because it is what a half-finished move looks like and it
/// is the one thing on this response a person has to act on rather than wait through: the message may be in the
/// destination, in the source, or in both, and MailFathom will not guess which.
/// </remarks>
internal sealed record ClientMailChangeStateResponse(
    Guid RecordId,
    Guid StoredEmailId,
    string Mutation,
    string State,
    bool OutcomeUnknown,
    int AttemptCount,
    int? LastFailure,
    DateTimeOffset RecordedAt,
    DateTimeOffset StateChangedAt)
{
    /// <summary>Describes one change on the wire.</summary>
    /// <param name="progress">What the use case read.</param>
    /// <returns>The response entry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="progress" /> is <see langword="null" />.</exception>
    internal static ClientMailChangeStateResponse For(MailboxChangeProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        return new ClientMailChangeStateResponse(
            progress.RecordId.Value,
            progress.StoredEmailId.Value,
            progress.Mutation.Name,
            progress.Lifecycle.Name,
            progress.IsOutcomeUnknown,
            progress.AttemptCount,
            progress.LastFailure?.Value,
            progress.RecordedAt,
            progress.StageChangedAt);
    }
}

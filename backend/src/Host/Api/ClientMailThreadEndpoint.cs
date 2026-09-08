// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.BrowseThread;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves one conversation as the document a thread screen is drawn from.</summary>
/// <remarks>
/// <para>
/// A conversation is the one mail screen a folder cannot be the scope of: the question is in the inbox, the answer is
/// in the sent folder, and a forwarded copy is somewhere else again. So the route names a conversation and nothing
/// else — no account, no folder — and reads it across everything the signed-in user owns, junk included, because a
/// reply that landed in junk is still part of the exchange somebody is reading.
/// </para>
/// <para>
/// The document is one request. Its messages arrive in the conversation's own order, each drawn exactly as a list row
/// is drawn and carrying the opening of what that message added with the quoted history trimmed off, and the
/// participants arrive beside them so a header costs no walk over the messages.
/// </para>
/// <para>
/// A caller that draws the messages rather than a list of them asks for them with <c>content=true</c>, and each message
/// then arrives with what the message route and the body route answer for it — the headers it displays, the verdict on
/// who sent it, the files it carries, and the body as words and as the reduced document tree. That is what makes
/// reading a correspondence one request rather than one request per message somebody reveals. It costs what reading
/// that many messages costs, so it is bounded by the same ceiling any content read is held to
/// (<see cref="GetEmailContentRequest.MaximumEmails" />): a page asking for more messages than that with their content
/// is refused rather than served half-drawn, and a longer conversation is read on with the cursor.
/// </para>
/// <para>
/// It speaks to no mail server, so a request from a browser cannot wait on IMAP and cannot set the remote <c>\Seen</c>
/// flag.
/// </para>
/// </remarks>
internal static class ClientMailThreadEndpoint
{
    /// <summary>The route reporting one page of one conversation, relative to the client prefix.</summary>
    internal const string MailThreadRoute = "/threads/{threadId:guid}";

    /// <summary>Maps the route into the client group, so it inherits the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientMailThread(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(MailThreadRoute, ReadThreadAsync)
            .RequirePermission(MailFathomPermission.MailRead);
    }

    /// <summary>Serves one page of one of the acting user's conversations, or reports what was wrong with the request.</summary>
    /// <param name="threadId">The conversation to read, as a message row published it.</param>
    /// <param name="pageSize">How many messages the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor a previous page returned, or <see langword="null" /> for the start of the conversation.</param>
    /// <param name="content">Whether each message arrives with what it says, which is what a caller drawing the conversation asks for.</param>
    /// <param name="thread">Reads the conversation, for a caller the read's own grant admits.</param>
    /// <param name="messages">Reads the messages themselves, where the caller asked for them.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, <c>400</c> naming what was wrong with the request, <c>404</c> where this user has no such conversation, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.read</c>.</returns>
    /// <remarks>
    /// A conversation this user does not hold and one no deployment ever held answer identically, so nothing here tells
    /// a caller that somebody else's exchange exists. The identifier is matched as a UUID by the route itself, which is
    /// what makes text that names no conversation at all the same <c>404</c> rather than a refusal of its own.
    /// </remarks>
    internal static async Task<Results<Ok<ClientMailThreadResponse>, NotFound, ProblemHttpResult>> ReadThreadAsync(
        [FromRoute] Guid threadId,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromQuery] bool? content,
        [FromServices] MailThreadBrowser thread,
        [FromServices] EmailContentReader messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(messages);

        if (threadId == Guid.Empty)
        {
            return TypedResults.NotFound();
        }

        var drawing = content is true;

        // The whole range rather than the ceiling alone, so that a page of zero is refused in the words of the read it
        // was asked for: the general bound below would otherwise answer a drawn conversation with the 1-to-100 range,
        // which is not the range that request had.
        if (drawing && pageSize is { } asked && asked is < 1 or > GetEmailContentRequest.MaximumEmails)
        {
            return Refuse(
                $"A conversation read with its messages holds between 1 and {GetEmailContentRequest.MaximumEmails} of them, "
                + "so ask for a page within that and read on with the cursor.");
        }

        var request = new BrowseThreadRequest
        {
            ThreadId = EmailThreadId.Create(threadId),
            PageSize = drawing ? pageSize ?? GetEmailContentRequest.MaximumEmails : pageSize,
            Cursor = cursor,
        };

        try
        {
            var page = await thread.BrowsePageAsync(request, cancellationToken);

            if (page is null)
            {
                return TypedResults.NotFound();
            }

            var drawn = drawing ? await ReadMessagesAsync(page, messages, cancellationToken) : [];

            return TypedResults.Ok(ClientMailThreadResponse.For(page, drawn));
        }
        catch (MailboxQueryCursorMalformedException)
        {
            return Refuse("The cursor is not one this deployment issued.");
        }
        catch (MailboxQueryCursorFilterMismatchException)
        {
            return Refuse("The cursor was issued for a different conversation, so it has to be presented against the one it came from.");
        }
        catch (EmailThreadCursorMessageMissingException)
        {
            return Refuse("The cursor names a message this conversation no longer shows, so the conversation has to be read from its beginning.");
        }
        catch (MailboxQueryPageSizeOutOfRangeException)
        {
            return Refuse($"A page holds between 1 and {MailboxQueryPageSize.MaximumValue} messages.");
        }
    }

    /// <summary>Reads the page's messages themselves, so the conversation is drawn from one request rather than from one per message.</summary>
    /// <param name="page">The page the conversation answered with.</param>
    /// <param name="messages">Reads the messages from the local copy, under the caller's own grant.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns>What was read, in the order the page named its messages, with a message whose local copy is damaged absent.</returns>
    private static async Task<IReadOnlyList<ReadEmailContent>> ReadMessagesAsync(
        BrowsedThread page,
        EmailContentReader messages,
        CancellationToken cancellationToken)
    {
        if (page.Messages.Count == 0)
        {
            return [];
        }

        var read = await messages.ReadContentAsync(
            ContentRequestFor([.. page.Messages.Select(message => message.Email.StoredEmailId)]),
            cancellationToken);

        return [.. read.Emails.Select(outcome => outcome.Content).OfType<ReadEmailContent>()];
    }

    /// <summary>Composes the read a drawn conversation makes, which asks for exactly what the two message routes ask for between them.</summary>
    /// <param name="storedEmailIds">The page's messages, in the conversation's own order.</param>
    /// <returns>The request the use case is asked with.</returns>
    /// <remarks>
    /// Named rather than inlined for the reason the body route's is: what it declines is the point. It asks for the
    /// reduced tree, because that is what a message is drawn from, and for neither the sender's own markup nor a minted
    /// download link — the first is the surface a reader opens per message, and the second is a bearer credential this
    /// response would otherwise hand out once per message in a conversation. The reader's ask for remote pictures is
    /// per message as well, so a conversation is drawn with none of them resolved.
    /// </remarks>
    internal static GetEmailContentRequest ContentRequestFor(IReadOnlyList<StoredEmailId> storedEmailIds) =>
        GetEmailContentRequest.Create(storedEmailIds) with { IncludeMailDocument = true };

    /// <summary>States what a caller has to change, without echoing what they sent.</summary>
    private static ProblemHttpResult Refuse(string stated) =>
        TypedResults.Problem(stated, statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>One conversation as the client endpoint serves it, and one page of its messages.</summary>
/// <param name="ThreadId">The conversation, as the request named it.</param>
/// <param name="Messages">The page's messages, in the conversation's own order.</param>
/// <param name="Participants">Everybody who wrote in the conversation, in the order they first wrote in it.</param>
/// <param name="MessageCount">How many messages the conversation holds of those this caller may see.</param>
/// <param name="MoreMessagesNotAssembled">Whether the conversation runs past what one read assembles at all.</param>
/// <param name="MoreParticipantsNotNamed">Whether the conversation has authors the list does not name.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end of the conversation.</param>
/// <param name="PageSize">How many messages the read ran under, which is what the request asked for or the default it took.</param>
/// <remarks>
/// Everything outside <c>messages</c> describes the whole conversation rather than the page, so a client draws a thread
/// header from the first page and keeps it accurate without holding the rest. The two counts are of what this caller may
/// see: a message in a folder an operator withheld is in neither.
/// </remarks>
internal sealed record ClientMailThreadResponse(
    Guid ThreadId,
    IReadOnlyList<ClientMailThreadEmailResponse> Messages,
    IReadOnlyList<ClientMailThreadParticipantResponse> Participants,
    int MessageCount,
    bool MoreMessagesNotAssembled,
    bool MoreParticipantsNotNamed,
    string? NextCursor,
    int PageSize)
{
    /// <summary>Describes one page of one conversation for the wire.</summary>
    /// <param name="thread">The page the use case read.</param>
    /// <param name="drawn">The messages themselves, where the caller asked for them, and empty where it did not.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="thread" /> or <paramref name="drawn" /> is <see langword="null" />.</exception>
    internal static ClientMailThreadResponse For(BrowsedThread thread, IReadOnlyList<ReadEmailContent> drawn)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(drawn);

        var byIdentity = drawn.ToDictionary(message => message.StoredEmailId);

        return new ClientMailThreadResponse(
            thread.ThreadId.Value,
            [.. thread.Messages.Select(message => ClientMailThreadEmailResponse.For(
                message,
                thread.MessageCount,
                byIdentity.GetValueOrDefault(message.Email.StoredEmailId)))],
            [.. thread.Participants.Select(ClientMailThreadParticipantResponse.For)],
            thread.MessageCount,
            thread.MoreMessagesNotAssembled,
            thread.MoreParticipantsNotNamed,
            thread.NextCursor,
            thread.PageSize);
    }
}

/// <summary>One message of a conversation, and where it sits in that conversation.</summary>
/// <param name="Position">The zero-based place the message holds in the conversation's order.</param>
/// <param name="AnsweredId">The message this one answers among the ones shown, or <see langword="null" /> where it is a root of what is shown.</param>
/// <param name="Email">The message itself, in the same shape a list row carries.</param>
/// <param name="Message">What a reading pane draws around the message, or <see langword="null" /> where the caller asked for a list of messages rather than for the messages.</param>
/// <param name="Body">What the message says, or <see langword="null" /> for the same reason.</param>
/// <remarks>
/// <para>
/// <c>email</c> is the mail list route's own row, field for field, so a client parses one message across this surface
/// and the two routes cannot come to disagree about one message. Its <c>preview</c> is what this message added, with
/// the quoted history and the signature block trimmed off, and its <c>id</c> is what the whole message — quoted history,
/// body and attachments — is reached by.
/// </para>
/// <para>
/// <c>message</c> and <c>body</c> are the message route's and the body route's own answers, field for field, for the
/// same reason: a conversation drawn from a shape of its own would be a second contract for one message, and the two
/// would come to disagree. What they repeat of the row is what those two routes already publish beside it — a client
/// draws the head from one and the row's preview from the other, and neither is derived here.
/// </para>
/// <para>
/// <c>answeredId</c> names a message the caller can see. One whose parent sits in a folder an operator withheld is
/// published as a root naming nothing, so the withheld message is not disclosed by the gap it would leave.
/// </para>
/// </remarks>
internal sealed record ClientMailThreadEmailResponse(
    int Position,
    Guid? AnsweredId,
    ClientMailTimelineEntryResponse Email,
    ClientMailMessageResponse? Message,
    ClientMailBodyResponse? Body)
{
    /// <summary>Describes one message of a conversation for the wire.</summary>
    /// <param name="message">The message the use case read.</param>
    /// <param name="threadMessageCount">How many messages the conversation holds of those this caller may see.</param>
    /// <param name="drawn">The message itself, where the caller asked for it and its local copy could be read.</param>
    /// <returns>The response body.</returns>
    /// <remarks>
    /// <para>
    /// The conversation's own count is what the row's <c>threadMessageCount</c> carries here, because that is what the
    /// field means and every message of one conversation is in the same conversation. A client drawing a message of a
    /// thread from this row therefore reads the same number the header above it reads — which is the assembled count
    /// rather than the counted one, so a correspondence past
    /// <see cref="Application.Emails.Threads.IEmailThreadReader.MaximumAssembledEmails" /> reads here as the bound with
    /// <c>moreMessagesNotAssembled</c> beside it and reads on a list row as its real length. Agreeing with the list
    /// instead would mean counting the conversation a second time to publish a number this route already states.
    /// </para>
    /// <para>
    /// A message the read could not open is published as the row alone rather than withheld from the conversation:
    /// what a reader is owed is the correspondence with a gap in it they can still open on its own, and a client
    /// reading a message this answer did not carry falls back to the two routes that serve one.
    /// </para>
    /// </remarks>
    internal static ClientMailThreadEmailResponse For(
        BrowsedThreadEmail message,
        int threadMessageCount,
        ReadEmailContent? drawn) => new(
        message.Position,
        message.AnsweredStoredEmailId?.Value,
        ClientMailTimelineEntryResponse.For(
            message.Email,
            message.Contribution,
            message.Enrichment,
            threadMessageCount),
        drawn is null ? null : ClientMailMessageResponse.For(drawn),
        drawn is null ? null : ClientMailBodyResponse.For(drawn, remoteImagesRequested: false));
}

/// <summary>Somebody who has written in the conversation, and how much of it is theirs.</summary>
/// <param name="Address">The address they wrote from, as their messages wrote it.</param>
/// <param name="DisplayName">The name their most recent message wrote, or <see langword="null" /> where none of them carried one.</param>
/// <param name="MessageCount">How many of the conversation's messages they sent.</param>
/// <remarks>
/// An author rather than an addressee, which is what a thread header draws. It is derived from the whole conversation
/// rather than from the page in hand, which is the point of publishing it: a client deriving it would be paging a
/// conversation to draw its header.
/// </remarks>
internal sealed record ClientMailThreadParticipantResponse(string Address, string? DisplayName, int MessageCount)
{
    /// <summary>Describes one participant for the wire.</summary>
    /// <param name="participant">The participant the use case named.</param>
    /// <returns>The response body.</returns>
    internal static ClientMailThreadParticipantResponse For(ThreadParticipant participant) => new(
        participant.Address,
        participant.DisplayName,
        participant.MessageCount);
}

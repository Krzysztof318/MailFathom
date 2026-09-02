// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.BrowseThread;
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
/// else — no account, no folder — and reads it across everything the signed-in owner owns, junk included, because a
/// reply that landed in junk is still part of the exchange somebody is reading.
/// </para>
/// <para>
/// The document is one request. Its messages arrive in the conversation's own order, each drawn exactly as a list row
/// is drawn and carrying the opening of what that message added with the quoted history trimmed off, and the
/// participants arrive beside them so a header costs no walk over the messages. What it never carries is a body: the
/// whole of a message, quoted history included, is a request of its own naming the identity a row already carries.
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

    /// <summary>Serves one page of one of the acting owner's conversations, or reports what was wrong with the request.</summary>
    /// <param name="threadId">The conversation to read, as a message row published it.</param>
    /// <param name="pageSize">How many messages the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor a previous page returned, or <see langword="null" /> for the start of the conversation.</param>
    /// <param name="thread">Reads the conversation, for a caller the read's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, <c>400</c> naming what was wrong with the request, <c>404</c> where this owner has no such conversation, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.read</c>.</returns>
    /// <remarks>
    /// A conversation this owner does not hold and one no deployment ever held answer identically, so nothing here tells
    /// a caller that somebody else's exchange exists. The identifier is matched as a UUID by the route itself, which is
    /// what makes text that names no conversation at all the same <c>404</c> rather than a refusal of its own.
    /// </remarks>
    internal static async Task<Results<Ok<ClientMailThreadResponse>, NotFound, ProblemHttpResult>> ReadThreadAsync(
        [FromRoute] Guid threadId,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] MailThreadBrowser thread,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(thread);

        if (threadId == Guid.Empty)
        {
            return TypedResults.NotFound();
        }

        var request = new BrowseThreadRequest
        {
            ThreadId = EmailThreadId.Create(threadId),
            PageSize = pageSize,
            Cursor = cursor,
        };

        try
        {
            var page = await thread.BrowsePageAsync(request, cancellationToken);

            return page is null
                ? TypedResults.NotFound()
                : TypedResults.Ok(ClientMailThreadResponse.For(page));
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
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="thread" /> is <see langword="null" />.</exception>
    internal static ClientMailThreadResponse For(BrowsedThread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        return new ClientMailThreadResponse(
            thread.ThreadId.Value,
            [.. thread.Messages.Select(ClientMailThreadEmailResponse.For)],
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
/// <remarks>
/// <para>
/// <c>email</c> is the mail list route's own row, field for field, so a client parses one message across this surface
/// and the two routes cannot come to disagree about one message. Its <c>preview</c> is what this message added, with
/// the quoted history and the signature block trimmed off, and its <c>id</c> is what the whole message — quoted history,
/// body and attachments — is reached by. Nothing is repeated here under a second name.
/// </para>
/// <para>
/// <c>answeredId</c> names a message the caller can see. One whose parent sits in a folder an operator withheld is
/// published as a root naming nothing, so the withheld message is not disclosed by the gap it would leave.
/// </para>
/// </remarks>
internal sealed record ClientMailThreadEmailResponse(
    int Position,
    Guid? AnsweredId,
    ClientMailTimelineEntryResponse Email)
{
    /// <summary>Describes one message of a conversation for the wire.</summary>
    /// <param name="message">The message the use case read.</param>
    /// <returns>The response body.</returns>
    internal static ClientMailThreadEmailResponse For(BrowsedThreadEmail message) => new(
        message.Position,
        message.AnsweredStoredEmailId?.Value,
        ClientMailTimelineEntryResponse.For(message.Email, message.Contribution));
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

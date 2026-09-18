// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Correspondence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves what the signed-in account's own mail already holds about one contact.</summary>
/// <remarks>
/// <para>
/// It is the second half of reading one contact: the record itself says who somebody is, and this says what the two of
/// them have been doing — the conversations the person's addresses appear in, and the documents they sent. Both are
/// computed on the read from the mail index, so nothing here is a store and nothing here can fall behind the mailbox.
/// </para>
/// <para>
/// It stands beside the contact's own route rather than inside it, because the two cost different things: a record is a
/// row, and a correlation is a bounded walk of a year of mail. A client that only lists contacts never pays for one.
/// </para>
/// <para>
/// Two grants are involved and each is asked by the use case that needs it. The route is published under the mail one,
/// because what the answer carries is mail; reading the contact is the book's own decision, so a caller granted mail
/// and not contacts is refused naming the contacts grant rather than served an empty answer.
/// </para>
/// <para>
/// It speaks to no mail server, so a request from a browser cannot wait on IMAP and cannot set the remote <c>\Seen</c>
/// flag.
/// </para>
/// </remarks>
internal static class ClientContactCorrespondenceEndpoint
{
    /// <summary>The route reporting what mail holds about one contact, relative to the client prefix.</summary>
    internal const string ContactCorrespondenceRoute = "/contacts/{contactId:guid}/correspondence";

    /// <summary>Maps the route into the client group, so it inherits the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientContactCorrespondence(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(ContactCorrespondenceRoute, ReadCorrespondenceAsync)
            .RequirePermission(MailFathomPermission.MailRead);
    }

    /// <summary>Serves the correlation for one of the acting user's own contacts.</summary>
    /// <param name="contactId">The contact to correlate, as the book published it.</param>
    /// <param name="contacts">Reads the contact, for a caller the book's own grant admits.</param>
    /// <param name="correspondence">Correlates the contact's addresses with the mail this caller may read.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the correlation, <c>404</c> where no book in this caller's scope holds the contact, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.read</c> or <c>mailfathom.mail.contacts.read</c>.</returns>
    /// <remarks>
    /// A contact this caller does not hold and one no deployment ever held answer identically, so nothing here tells a
    /// caller that somebody else's correspondent exists. A contact the mail says nothing about answers with two empty
    /// lists rather than with a refusal, which is the accurate answer: the person is in the book and the window holds
    /// no mail of theirs.
    /// </remarks>
    internal static async Task<Results<Ok<ClientContactCorrespondenceResponse>, NotFound>> ReadCorrespondenceAsync(
        [FromRoute] Guid contactId,
        [FromServices] ContactBookReader contacts,
        [FromServices] ContactCorrespondenceReader correspondence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        ArgumentNullException.ThrowIfNull(correspondence);

        if (contactId == Guid.Empty)
        {
            return TypedResults.NotFound();
        }

        var contact = await contacts.FindAsync(ContactId.Create(contactId), cancellationToken);

        if (contact is null)
        {
            return TypedResults.NotFound();
        }

        var correlated = await correspondence.ReadAsync(contact, cancellationToken);

        return TypedResults.Ok(ClientContactCorrespondenceResponse.For(contactId, correlated));
    }
}

/// <summary>What one contact's correlation carries, as the client endpoint serves it.</summary>
/// <param name="ContactId">The contact, as the request named it.</param>
/// <param name="Threads">The most recent conversations naming one of their addresses, newest first.</param>
/// <param name="Documents">The most recent documents they sent, newest first.</param>
/// <remarks>
/// Each list is bounded and neither is paged. What an opened contact draws is a card rather than a mailbox, so a reader
/// who wants the rest of an exchange opens the conversation, and one who wants everything a person ever sent searches
/// for their address.
/// </remarks>
internal sealed record ClientContactCorrespondenceResponse(
    Guid ContactId,
    IReadOnlyList<ClientContactThreadResponse> Threads,
    IReadOnlyList<ClientContactDocumentResponse> Documents)
{
    /// <summary>Describes one contact's correlation for the wire.</summary>
    /// <param name="contactId">The contact the request named.</param>
    /// <param name="correspondence">What the use case read.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="correspondence" /> is <see langword="null" />.</exception>
    internal static ClientContactCorrespondenceResponse For(Guid contactId, ContactCorrespondence correspondence)
    {
        ArgumentNullException.ThrowIfNull(correspondence);

        return new ClientContactCorrespondenceResponse(
            contactId,
            [.. correspondence.Threads.Select(ClientContactThreadResponse.For)],
            [.. correspondence.Documents.Select(ClientContactDocumentResponse.For)]);
    }
}

/// <summary>One conversation a contact takes part in.</summary>
/// <param name="ThreadId">The conversation, which the conversation route is asked with.</param>
/// <param name="LatestMessageId">The most recent message of it naming this person, which the message route is asked with.</param>
/// <param name="Subject">What that message was about, or <see langword="null" /> where it carried none.</param>
/// <param name="LastCorrespondedAt">When that message arrived.</param>
internal sealed record ClientContactThreadResponse(
    Guid ThreadId,
    Guid LatestMessageId,
    string? Subject,
    DateTimeOffset LastCorrespondedAt)
{
    /// <summary>Describes one conversation for the wire.</summary>
    /// <param name="thread">The conversation the use case read.</param>
    /// <returns>The response body.</returns>
    internal static ClientContactThreadResponse For(CorrespondingThread thread) => new(
        thread.ThreadId.Value,
        thread.LatestStoredEmailId.Value,
        thread.Subject,
        thread.LastCorrespondedAt);
}

/// <summary>One document a contact sent.</summary>
/// <param name="MessageId">The message the file arrived on, which the attachment route is asked with.</param>
/// <param name="Position">Where the file sits in that message's walk, which is the other half of what the attachment route is asked with.</param>
/// <param name="FileName">The name the sender wrote, or <see langword="null" /> where the part carried none.</param>
/// <param name="MediaType">The type the sender declared, which draws an icon and is never trusted as the file's content.</param>
/// <param name="ReceivedAt">When the message carrying it arrived.</param>
internal sealed record ClientContactDocumentResponse(
    Guid MessageId,
    int Position,
    string? FileName,
    string MediaType,
    DateTimeOffset ReceivedAt)
{
    /// <summary>Describes one document for the wire.</summary>
    /// <param name="document">The document the use case read.</param>
    /// <returns>The response body.</returns>
    internal static ClientContactDocumentResponse For(CorrespondingDocument document) => new(
        document.StoredEmailId.Value,
        document.AttachmentPosition,
        document.FileName,
        document.DeclaredMediaType,
        document.ReceivedAt);
}

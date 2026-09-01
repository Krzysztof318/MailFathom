// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves everything a reading pane draws around a message, and none of what it draws inside one.</summary>
/// <remarks>
/// <para>
/// It is the other half of the pane. The body route serves the message as words and as a document tree, and this serves
/// what a screen puts above and beside them: the headers the message displays, what this deployment established about
/// the author it displays, the files it carries, and which forms of its body exist to be asked for. The two are separate
/// requests because they are separately expensive and separately cacheable by a client — a pane draws the header block
/// as soon as this answers, whatever the body costs.
/// </para>
/// <para>
/// No octet of a file is here, at any size and in any encoding. What an attachment carries is a route of its own naming
/// the position this response gave it, which is what keeps opening a message the same cost whether the sender attached
/// a note or a video.
/// </para>
/// <para>
/// The pictures the message displays inside its own body are not published here either, and are not a route: the body
/// route resolves each <c>cid:</c> reference against the message's own parts while it reduces the body, so a sender's
/// own images arrive drawn rather than as references a pane would have to fetch — and a remote address is removed
/// there rather than turned into something this surface would resolve.
/// </para>
/// <para>
/// It speaks to no mail server, so a request from a browser cannot wait on IMAP and cannot set the remote <c>\Seen</c>
/// flag. Everything it answers with is read from the local copy through the same use case the tool surface reads a
/// message with, under the same grant.
/// </para>
/// </remarks>
internal static class ClientMailMessageEndpoint
{
    /// <summary>The route reporting one message, relative to the client prefix.</summary>
    internal const string MailMessageRoute = "/messages/{storedEmailId:guid}";

    /// <summary>Maps the route into the client group, so it inherits the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientMailMessage(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(MailMessageRoute, ReadMessageAsync)
            .RequirePermission(MailFathomPermission.MailRead);
    }

    /// <summary>Serves one of the acting owner's messages, or reports that there is no such message.</summary>
    /// <param name="storedEmailId">The message to read, as a list row or a conversation published it.</param>
    /// <param name="content">Reads the message from the local copy, for a caller the read's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the message, <c>404</c> where this owner has no such message, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.read</c>.</returns>
    /// <remarks>
    /// A message this owner does not hold and one no deployment ever held answer identically, so nothing here tells a
    /// caller that somebody else's mail exists. A local copy that is damaged or missing answers the same way as well,
    /// having recorded the repair request the use case records for it.
    /// </remarks>
    internal static async Task<Results<Ok<ClientMailMessageResponse>, NotFound>> ReadMessageAsync(
        [FromRoute] Guid storedEmailId,
        [FromServices] EmailContentReader content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (storedEmailId == Guid.Empty)
        {
            return TypedResults.NotFound();
        }

        var result = await content.ReadContentAsync(RequestFor(storedEmailId), cancellationToken);

        return result.Emails[0].Content is { } message
            ? TypedResults.Ok(ClientMailMessageResponse.For(message))
            : TypedResults.NotFound();
    }

    /// <summary>Composes the read this route makes, which asks for a description and for no representation at all.</summary>
    /// <param name="storedEmailId">The message to read.</param>
    /// <returns>The request the use case is asked with.</returns>
    /// <remarks>
    /// Named rather than inlined for the reason the body route's is: what it declines is the point. It asks for no
    /// markup, no reduced document, and no minted link — the pictures and the words belong to the body route, and a
    /// capability nobody asked for is a bearer credential this response would be handing out. The parse still happens,
    /// because the headers and the file descriptions are read from the stored message rather than from the row.
    /// </remarks>
    internal static GetEmailContentRequest RequestFor(Guid storedEmailId) =>
        GetEmailContentRequest.Create([StoredEmailId.Create(storedEmailId)]);
}

/// <summary>One message as the client endpoint serves it, without its body and without any file it carries.</summary>
/// <param name="StoredEmailId">The message, as the request named it.</param>
/// <param name="Account">The configured account the message was read from.</param>
/// <param name="Folder">MailFathom's own name for the folder the message was read from.</param>
/// <param name="ThreadId">The conversation the message belongs to, or <see langword="null" /> where nothing has placed it in one.</param>
/// <param name="SizeOctets">The size the mail server reported for the whole message, which no sum over the parts reproduces.</param>
/// <param name="Headers">What the message displays above its body.</param>
/// <param name="Body">Whether there is a body to draw, and which forms of it the sender wrote.</param>
/// <param name="Sender">What this deployment established about the author the message displays.</param>
/// <param name="Attachments">One entry per file the message carries, described and carrying none of what it holds.</param>
/// <param name="Carried">The counts for everything the message carries besides its body, or <see langword="null" /> where nothing has ever read its parts.</param>
/// <param name="Unread">Whether the mail server last showed the message as unseen.</param>
/// <param name="Flagged">Whether the mail server last showed the message as flagged.</param>
/// <param name="Answered">Whether the mail server last showed the message as answered.</param>
/// <remarks>
/// All of it is mail content and personal data, so none of it reaches a log, a span attribute, or a telemetry event,
/// here or anywhere it is carried afterwards.
/// </remarks>
internal sealed record ClientMailMessageResponse(
    Guid StoredEmailId,
    string Account,
    string Folder,
    Guid? ThreadId,
    long SizeOctets,
    ClientMailMessageHeadersResponse Headers,
    ClientMailMessageBodyResponse Body,
    ClientMailSenderVerdictResponse Sender,
    IReadOnlyList<ClientMailAttachmentResponse> Attachments,
    ClientMailCarriedResponse? Carried,
    bool Unread,
    bool Flagged,
    bool Answered)
{
    /// <summary>Describes one message for the wire.</summary>
    /// <param name="message">The message the use case read.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message" /> is <see langword="null" />.</exception>
    internal static ClientMailMessageResponse For(ReadEmailContent message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new ClientMailMessageResponse(
            message.StoredEmailId.Value,
            message.AccountId.Value,
            message.FolderAlias.Value,
            message.Thread?.ThreadId.Value,
            message.SizeOctets,
            ClientMailMessageHeadersResponse.For(message.Headers),
            ClientMailMessageBodyResponse.For(message.Body),
            ClientMailSenderVerdictResponse.For(message.SenderVerification, message.SenderAuthenticationEvidence),
            [.. message.Attachments.Select((attachment, position) =>
                ClientMailAttachmentResponse.For(attachment, position))],
            message.AttachmentSummary is { } carried ? ClientMailCarriedResponse.For(carried) : null,
            !message.RemoteFlags.IsSeen,
            message.RemoteFlags.IsFlagged,
            message.RemoteFlags.IsAnswered);
    }
}

/// <summary>The headers one message displays above its body.</summary>
/// <param name="Subject">The decoded subject, or <see langword="null" /> where the message carried none.</param>
/// <param name="SentAt">When the sender says the message was sent, or <see langword="null" /> where it wrote no usable date.</param>
/// <param name="ReceivedAt">When the last receiving hop recorded the message, or <see langword="null" /> where no header carried a usable date.</param>
/// <param name="Participants">Every usable address the message wrote, each paired with the header it appeared in.</param>
/// <param name="MessageId">The message's own identifier without its angle brackets, or <see langword="null" /> where it carried none.</param>
/// <param name="InReplyTo">The identifier of the message this one answers, or <see langword="null" /> where it answers none.</param>
/// <param name="References">The identifiers placing the message in a conversation, in the order the header listed them.</param>
/// <remarks>
/// These are parsed from the stored message rather than read off the columns a list row is served from, which is why a
/// display name, a <c>Bcc</c> the message carried for its own recipient, and the threading identifiers are here and not
/// on a row. A message whose content the size limit kept out of storage carries the narrower set a row can answer for,
/// which is what its body availability says.
/// </remarks>
internal sealed record ClientMailMessageHeadersResponse(
    string? Subject,
    DateTimeOffset? SentAt,
    DateTimeOffset? ReceivedAt,
    IReadOnlyList<ClientMailParticipantResponse> Participants,
    string? MessageId,
    string? InReplyTo,
    IReadOnlyList<string> References)
{
    /// <summary>Describes one message's headers for the wire.</summary>
    /// <param name="headers">The headers the read produced.</param>
    /// <returns>The response body.</returns>
    internal static ClientMailMessageHeadersResponse For(EmailContentHeaders headers) => new(
        headers.Subject,
        headers.SentAt,
        headers.ReceivedAt,
        [.. headers.Participants.Select(ClientMailParticipantResponse.For)],
        headers.ThreadReferences.MessageId,
        headers.ThreadReferences.InReplyTo,
        headers.ThreadReferences.References);
}

/// <summary>One address a message wrote, and the header it wrote it in.</summary>
/// <param name="Role">The header the address appeared in, as the role's own name.</param>
/// <param name="Address">The normalized address.</param>
/// <param name="DisplayName">The name written beside the address, or <see langword="null" /> where none was written.</param>
internal sealed record ClientMailParticipantResponse(string Role, string Address, string? DisplayName)
{
    /// <summary>Describes one participant for the wire.</summary>
    /// <param name="participant">The participant the read produced.</param>
    /// <returns>The response body.</returns>
    internal static ClientMailParticipantResponse For(EmailParticipant participant) => new(
        participant.Role.ToString(),
        participant.Address.Address,
        participant.Address.DisplayName);
}

/// <summary>Whether one message has a body to draw, and which forms of it the sender wrote.</summary>
/// <param name="Availability">Whether the body could be read at all, or why it could not, as the state's own name.</param>
/// <param name="PlainText">Whether the sender wrote a plain-text part of their own.</param>
/// <param name="Html">Whether the sender wrote an HTML part, which is what the body route's document is reduced from.</param>
/// <remarks>
/// It says what the message carried rather than what a request would return, which is the part a client cannot work out
/// for itself: the body route answers with words for every readable message, deriving them from the markup where the
/// sender wrote no text part, so a pane that wanted to know whether there is a richer rendering to draw would be reading
/// a returned representation for a fact it does not carry. Both forms are absent for a body nothing could read, and the
/// availability beside them says which of the reasons applied.
/// </remarks>
internal sealed record ClientMailMessageBodyResponse(string Availability, bool PlainText, bool Html)
{
    /// <summary>Describes one message's body for the wire, without any of it.</summary>
    /// <param name="body">The body the read produced.</param>
    /// <returns>The response body.</returns>
    internal static ClientMailMessageBodyResponse For(EmailContentBody body) => new(
        body.Availability.ToString(),
        body.Forms.PlainText,
        body.Forms.Html);
}

/// <summary>What this deployment established about the author one message displays.</summary>
/// <param name="AuthorAuthentication">What the receiving mail server established about the displayed author, as the outcome's own name.</param>
/// <param name="DeploymentTrust">Whether this deployment recognizes that author, as the level's own name.</param>
/// <param name="AuthenticatedDomain">The domain that actually authenticated, or <see langword="null" /> where none did.</param>
/// <remarks>
/// <para>
/// The two outcomes are published side by side and are never collapsed into one value, because a screen drawing a single
/// badge from them would have to invent the rule that combines them. One is a fact a receiving server established about
/// the message, and the other is this deployment's own classification of the author it established; an authenticated
/// author nobody has named is the ordinary state of legitimate mail and carries the same trust value as one whose
/// authentication failed outright.
/// </para>
/// <para>
/// The domain travels with them so that a reading pane can name who actually sent a message rather than repeating the
/// <c>From</c> value the message displays, which is the one thing an impersonation gets wrong. It is stated and never
/// judged: whether it differs from the displayed domain says nothing on its own — a provider that signs as itself while
/// the author's own domain passes SPF is authenticated exactly as it appears — so what a reader acts on stays
/// <paramref name="AuthorAuthentication" />, and a client comparing the two would be evaluating a policy this deployment
/// deliberately does not.
/// </para>
/// <para>
/// It is read back as it was stored rather than derived here. Nothing on this path re-reads a header, resolves DNS, or
/// evaluates a policy, so what a reader is shown is what extraction concluded about the authenticated author — never a
/// reading of the <c>From</c> header the message displays. The domain is personal data like the rest of this response.
/// </para>
/// </remarks>
internal sealed record ClientMailSenderVerdictResponse(
    string AuthorAuthentication,
    string DeploymentTrust,
    string? AuthenticatedDomain)
{
    /// <summary>Describes one message's sender verdict for the wire.</summary>
    /// <param name="verification">The verdict the read carried.</param>
    /// <param name="evidence">What that verdict was reached from, of which only the authenticated domain is published.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static ClientMailSenderVerdictResponse For(
        SenderVerification verification,
        SenderAuthenticationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(verification);
        ArgumentNullException.ThrowIfNull(evidence);

        return new ClientMailSenderVerdictResponse(
            verification.AuthorAuthentication.ToString(),
            verification.DeploymentTrust.ToString(),
            evidence.AuthenticatedDomain?.Value);
    }
}

/// <summary>One file a message carries, described and carrying none of what it holds.</summary>
/// <param name="Position">The zero-based place the file holds in this list, which is what the attachment route is asked with.</param>
/// <param name="FileName">The normalized file name, or <see langword="null" /> where the part carried no usable name.</param>
/// <param name="WasFileNameNormalized">Whether normalization had to rewrite what the message wrote.</param>
/// <param name="MediaType">What the part declares itself to be, which is what the sender wrote rather than a reading of the content.</param>
/// <param name="SizeOctets">How many octets the file holds once its transfer encoding is decoded, which is what the download returns.</param>
/// <remarks>
/// <para>
/// The position is the identity because it is the only stable one a message's parts have: MIME gives an attachment no
/// identifier, a <c>Content-ID</c> is optional and sender-chosen, and a file name is neither unique nor required. It is
/// this list's own order, which is the order the message's structure is walked, and the same order the attachment route
/// resolves a position against.
/// </para>
/// <para>
/// A file name is text a sender chose. It arrives normalized to a bare name — never a path, never a traversal segment,
/// never a control character — and <c>wasFileNameNormalized</c> says whether that rewrote anything, which is exactly the
/// case worth drawing carefully.
/// </para>
/// </remarks>
internal sealed record ClientMailAttachmentResponse(
    int Position,
    string? FileName,
    bool WasFileNameNormalized,
    string MediaType,
    long SizeOctets)
{
    /// <summary>Describes one attachment for the wire.</summary>
    /// <param name="attachment">The attachment the read described.</param>
    /// <param name="position">The place it holds in the read's own order.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="attachment" /> is <see langword="null" />.</exception>
    internal static ClientMailAttachmentResponse For(ReadEmailAttachment attachment, int position)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        return new ClientMailAttachmentResponse(
            position,
            attachment.Description.FileName?.Value,
            attachment.Description.FileName?.WasNormalized ?? false,
            attachment.Description.MediaType,
            attachment.Description.DecodedSizeOctets);
    }
}

/// <summary>The counts for everything one message carries besides its body.</summary>
/// <param name="AttachmentCount">How many parts are files a person would open.</param>
/// <param name="TotalSizeOctets">The decoded octets of those files together.</param>
/// <param name="InlineResourceCount">How many parts are resources the body embeds, which the body route draws inside the message rather than listing here.</param>
/// <param name="Encrypted">Whether the message carries encrypted content somewhere.</param>
/// <param name="UnverifiedSignature">Whether it carries a signature part, verified by nothing.</param>
/// <param name="UnexpandedTnefPart">Whether it carries a TNEF <c>winmail.dat</c> part that was recorded without being expanded.</param>
/// <remarks>
/// The counts are separate values because a message whose only non-body parts are inline resources or a signature
/// carries no attachments: collapsing them would draw a paperclip on every signed message and on every message with a
/// logo in its signature block.
/// </remarks>
internal sealed record ClientMailCarriedResponse(
    int AttachmentCount,
    long TotalSizeOctets,
    int InlineResourceCount,
    bool Encrypted,
    bool UnverifiedSignature,
    bool UnexpandedTnefPart)
{
    /// <summary>Describes what a message carries for the wire.</summary>
    /// <param name="carried">The counts the read produced.</param>
    /// <returns>The response body.</returns>
    internal static ClientMailCarriedResponse For(StoredEmailAttachmentSummary carried) => new(
        carried.AttachmentCount,
        carried.TotalSizeOctets,
        carried.InlineResourceCount,
        carried.IsEncrypted,
        carried.CarriesUnverifiedSignature,
        carried.ContainsUnexpandedTnefPart);
}

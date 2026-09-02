// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Domain.Delivery.Drafts;

namespace MailFathom.Host.Api;

/// <summary>What a client sends to write a draft, as a message of its own or as an answer to one this deployment holds.</summary>
/// <param name="Account">The account the draft belongs to, by its identifier or its display name, which an answer reads from the message it answers and therefore never states.</param>
/// <param name="Subject">The subject the author wrote, which an answer derives from the message it answers and therefore never states.</param>
/// <param name="PlainTextBody">The plain text the author wrote, which every stored draft carries.</param>
/// <param name="HtmlBody">The HTML alternative the author wrote, or <see langword="null" /> where they wrote none.</param>
/// <param name="To">The addresses named in the <c>To</c> header.</param>
/// <param name="Cc">The addresses named in the <c>Cc</c> header.</param>
/// <param name="Bcc">The addresses named in the <c>Bcc</c> header.</param>
/// <param name="AnsweredEmailId">The stored message being answered, or <see langword="null" /> for a message of its own.</param>
/// <param name="Answers">Which answer is being written — <c>senderOnly</c>, <c>everyone</c>, or <c>forward</c> — or <see langword="null" /> for a message of its own.</param>
/// <remarks>
/// <para>
/// The two shapes are one request because a revision has to be able to stay whichever shape the draft already is: a
/// reply re-derives its account, its subject, and its threading identifiers from the message it answers, so an edit
/// that arrived as a message of its own would quietly detach the answer from its conversation.
/// </para>
/// <para>
/// All of it is mail content and personal data — the addresses above all — so none of it reaches a log, a span
/// attribute, or a telemetry event, here or anywhere it is carried afterwards.
/// </para>
/// </remarks>
internal sealed record ClientDraftWriteRequest(
    string? Account,
    string? Subject,
    string? PlainTextBody,
    string? HtmlBody,
    IReadOnlyList<string>? To,
    IReadOnlyList<string>? Cc,
    IReadOnlyList<string>? Bcc,
    Guid? AnsweredEmailId,
    string? Answers);

/// <summary>The drafts one owner is writing, as the client endpoint serves them.</summary>
/// <param name="Drafts">The drafts, newest edit first.</param>
/// <param name="MaximumCount">How many the reading answers with at most, so a screen can say when it is showing all of them.</param>
/// <remarks>
/// It carries no cursor, for the reason <see cref="MailDraftDirectory" /> gives: drafts are what one person has open
/// rather than a corpus, and a walk through somebody's unsent mail is not a thing this surface offers.
/// </remarks>
internal sealed record ClientDraftListResponse(IReadOnlyList<ClientDraftResponse> Drafts, int MaximumCount)
{
    /// <summary>Describes one owner's drafts for the wire.</summary>
    /// <param name="drafts">What the reading answered with.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="drafts" /> is <see langword="null" />.</exception>
    internal static ClientDraftListResponse For(IReadOnlyList<MailDraftRecord> drafts)
    {
        ArgumentNullException.ThrowIfNull(drafts);

        return new ClientDraftListResponse(
            [.. drafts.Select(ClientDraftResponse.For)],
            MailDraftDirectory.MaximumCount);
    }
}

/// <summary>One draft as the client endpoint serves it, without the message it is stored as.</summary>
/// <param name="DraftId">The draft, which every later act names it by.</param>
/// <param name="Account">The configured account the draft belongs to and a send would go out as.</param>
/// <param name="Subject">The subject the current revision carries, empty where the author wrote none.</param>
/// <param name="Recipients">The people the draft is addressed to, which may be nobody.</param>
/// <param name="Attachments">The files staged against the draft, oldest upload first.</param>
/// <param name="ServerCopy">What the draft still owes that folder, as the stage's own name.</param>
/// <param name="Revision">Which revision the stored message is, counted from one.</param>
/// <param name="SizeOctets">How many octets of MIME the current revision is.</param>
/// <param name="ComposedAt">When the draft was first written down.</param>
/// <param name="RevisedAt">When it last changed, which is what a screen sorts by.</param>
/// <param name="SentAs">The queued send a promotion wrote, or <see langword="null" /> while the draft was never sent.</param>
/// <param name="Divergence">Why the copy in the folder stopped being one MailFathom may touch, or <see langword="null" /> while none has.</param>
/// <param name="LastFailureCode">The code of the failure the last attempt on the mailbox ended in, or <see langword="null" /> while none has failed.</param>
/// <remarks>
/// <para>
/// The body is not here at any size. A screen listing drafts draws what each is about and who it is for, and the words
/// are read when one of them is opened — which is why the subject and the recipients are on the record and everything
/// else the message says stays in the message.
/// </para>
/// <para>
/// Only the code of a failure is published and never its message, for the reason the record keeps only the code: a
/// message assembled at a failure site may repeat what a remote server wrote.
/// </para>
/// </remarks>
internal sealed record ClientDraftResponse(
    Guid DraftId,
    string Account,
    string Subject,
    IReadOnlyList<ClientDraftRecipientResponse> Recipients,
    IReadOnlyList<ClientDraftAttachmentResponse> Attachments,
    string ServerCopy,
    int Revision,
    long SizeOctets,
    DateTimeOffset ComposedAt,
    DateTimeOffset RevisedAt,
    Guid? SentAs,
    string? Divergence,
    int? LastFailureCode)
{
    /// <summary>Describes one draft for the wire.</summary>
    /// <param name="draft">The record the use case answered with.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="draft" /> is <see langword="null" />.</exception>
    internal static ClientDraftResponse For(MailDraftRecord draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return new ClientDraftResponse(
            draft.Id.Value,
            draft.AccountId.Value,
            draft.Subject,
            [.. draft.Recipients.Select(ClientDraftRecipientResponse.For)],
            [.. draft.Attachments.Select(ClientDraftAttachmentResponse.For)],
            draft.Stage.ToString(),
            draft.Revision,
            draft.MimeByteLength,
            draft.ComposedAt,
            draft.RevisedAt,
            draft.PromotedTo?.Value,
            draft.Divergence?.Reason.ToString(),
            draft.LastFailure?.Value);
    }
}

/// <summary>One person a draft is addressed to.</summary>
/// <param name="Role">The header the address is written in, as the role's own name.</param>
/// <param name="Address">The address the message would go to.</param>
/// <param name="DisplayName">The name written beside it, or <see langword="null" /> where none was written.</param>
/// <param name="Provenance">Where the address came from, as the provenance's own name, which is what a send's governance asks about.</param>
internal sealed record ClientDraftRecipientResponse(
    string Role,
    string Address,
    string? DisplayName,
    string Provenance)
{
    /// <summary>Describes one recipient for the wire.</summary>
    /// <param name="recipient">The recipient the record carries.</param>
    /// <returns>The response body.</returns>
    internal static ClientDraftRecipientResponse For(MailDraftRecipient recipient) => new(
        recipient.Recipient.Role.ToString(),
        recipient.Recipient.Address.Address,
        recipient.Recipient.Address.DisplayName,
        recipient.Provenance.ToString());
}

/// <summary>One file staged against a draft, described and carrying none of what it holds.</summary>
/// <param name="AttachmentId">The file, which taking it back off names it by.</param>
/// <param name="FileName">What the file is called.</param>
/// <param name="MediaType">What it declares itself to be, which is what the author's own client wrote.</param>
/// <param name="SizeOctets">How many octets it holds, before any transfer encoding a composition applies.</param>
/// <param name="StagedAt">When the upload was taken in.</param>
internal sealed record ClientDraftAttachmentResponse(
    Guid AttachmentId,
    string FileName,
    string MediaType,
    long SizeOctets,
    DateTimeOffset StagedAt)
{
    /// <summary>Describes one staged file for the wire.</summary>
    /// <param name="attachment">The file the record carries.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="attachment" /> is <see langword="null" />.</exception>
    internal static ClientDraftAttachmentResponse For(MailDraftAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        return new ClientDraftAttachmentResponse(
            attachment.Id.Value,
            attachment.FileName,
            attachment.MediaType,
            attachment.ByteLength,
            attachment.StagedAt);
    }
}

/// <summary>One draft opened for editing: what a listing already said about it, and the words it carries.</summary>
/// <param name="Draft">The record, exactly as the listing serves it.</param>
/// <param name="PlainTextBody">The plain text the stored message carries.</param>
/// <param name="HtmlBody">The HTML the stored message carries, or <see langword="null" /> where it carries none.</param>
/// <remarks>
/// The words are parsed out of the stored message rather than out of a second copy of them, so what an author is given
/// back to go on editing is what would actually be sent. It is the one draft route that loads a message, which is why
/// it is asked for one draft by identity.
/// </remarks>
internal sealed record ClientDraftReadingResponse(
    ClientDraftResponse Draft,
    string PlainTextBody,
    string? HtmlBody)
{
    /// <summary>Describes one opened draft for the wire.</summary>
    /// <param name="reading">The draft and its text, as the use case read them.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reading" /> is <see langword="null" />.</exception>
    internal static ClientDraftReadingResponse For(MailDraftReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return new ClientDraftReadingResponse(
            ClientDraftResponse.For(reading.Draft),
            reading.Text.PlainTextBody,
            reading.Text.HtmlBody);
    }
}

/// <summary>What became of the copies a mailbox held for a draft that was given up.</summary>
/// <param name="DraftId">The draft, as the request named it.</param>
/// <param name="Outcome">What the attempt on the mailbox did, as the outcome's own name.</param>
/// <param name="Divergence">Why the copy stopped being one MailFathom may touch, or <see langword="null" /> where none did.</param>
/// <param name="FailureCode">The code the attempt failed with, or <see langword="null" /> where it did not fail.</param>
/// <remarks>
/// The draft is given up here whatever the mailbox answered: a server that refuses to give a copy up leaves the message
/// as the owner's own to delete, and the outcome is what says so rather than a failure the client has to retry.
/// </remarks>
internal sealed record ClientDraftDiscardResponse(
    Guid DraftId,
    string Outcome,
    string? Divergence,
    int? FailureCode)
{
    /// <summary>Describes one give-up for the wire.</summary>
    /// <param name="result">What the filing did.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
    internal static ClientDraftDiscardResponse For(MailDraftFilingResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ClientDraftDiscardResponse(
            result.DraftId.Value,
            result.Outcome.ToString(),
            result.Divergence?.ToString(),
            result.Failure?.Value);
    }
}

/// <summary>What one draft became when it was sent, which is an ordinary queued send.</summary>
/// <param name="DraftId">The draft the send was written from.</param>
/// <param name="OutgoingEmail">The queued send, which the outbox routes name it by.</param>
/// <param name="Stage">Where the send stands, as the stage's own name, which for a fresh promotion is the queued one.</param>
/// <remarks>
/// Nothing has been transmitted when this answers. The message is queued, the copy in the drafts folder stands until
/// delivery settles it, and the outbox is where a client watches what became of it.
/// </remarks>
internal sealed record ClientDraftSendResponse(Guid DraftId, Guid OutgoingEmail, string Stage);

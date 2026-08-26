// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Mail.Delivery.Authoring;

/// <summary>Turns a stored email and what somebody wrote into the answer to it, ready to be composed.</summary>
/// <remarks>
/// <para>
/// A reply and a forward are the two sends that begin from mail this deployment already holds, and everything that
/// makes them correct is read out of that stored copy: the identifiers the answer threads by, the people it goes to,
/// the subject it carries, the text it quotes, and the files a forward brings with it. Each of those is a place a
/// plausible shortcut produces mail that looks right and is not — a guessed threading header puts the reply in a
/// conversation of its own in every recipient's mailbox, and a forward whose attachments were rebuilt from what was
/// recorded about them delivers files nobody sent.
/// </para>
/// <para>
/// It reaches no mail server, for the reason every read in this system does not: the use case holds no mailbox port, so
/// answering a message can neither fetch it again nor set the remote <c>\Seen</c> flag on it. The files a forward
/// carries come from <see cref="IEmailContentStore" />, which is the whole reason a forward is worth beginning from a
/// local copy at all.
/// </para>
/// <para>
/// The stored email is a permission boundary as well as a source. A folder an operator withheld from tools is outside
/// every mailbox read, and a reply must not become the path by which its content leaves — so an email nothing may read
/// is an email nothing may answer, and the refusal is the same not-found answer a read of it gives.
/// </para>
/// <para>
/// Whoever the author copies in may be named out of the contact book rather than by an address, and is resolved here
/// through the one resolution every author shares. That keeps a person the answer is addressed to an ordinary address by
/// the time anything is composed: an answer to a message cannot reach a mailbox a message answering nothing could not.
/// </para>
/// <para>
/// Nothing is written down here and nothing is sent. What comes back is an authored message, which the composition
/// turns into MIME and the outbox makes durable, exactly as it does for a message answering nothing.
/// </para>
/// </remarks>
public sealed class StoredEmailResponseAuthoring
{
    private readonly IStoredEmailSummaryReader summaryReader;
    private readonly IEmailContentStore contentStore;
    private readonly IEmailContentRenderer renderer;
    private readonly IEmailAttachmentContentReader attachmentContentReader;
    private readonly IEmailContentRepairRequestStore repairRequestStore;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly IOutgoingSenderIdentityReader senderIdentities;
    private readonly NamedRecipientResolver recipientResolver;
    private readonly OutgoingEmailBounds bounds;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the use case.</summary>
    /// <param name="summaryReader">Reads one stored email's summary by its identity.</param>
    /// <param name="contentStore">Reads the raw MIME stored for an email, with what was recorded about it.</param>
    /// <param name="renderer">Turns stored raw MIME into the headers, body, and attachment descriptions an answer is built from.</param>
    /// <param name="attachmentContentReader">Opens one attachment of that stored MIME so a forward can carry it.</param>
    /// <param name="repairRequestStore">Records durably that a local copy has to be fetched or read again.</param>
    /// <param name="scopeResolver">Answers whether a tool may read the mailbox the answered email was stored from.</param>
    /// <param name="senderIdentities">Resolves the address the answering account sends from, which is what a reply to all leaves out.</param>
    /// <param name="recipientResolver">Turns the people the author added into addresses, asking the contact book for the ones named as somebody.</param>
    /// <param name="bounds">What this deployment is willing to compose, which decides how much history and how many files an answer carries.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public StoredEmailResponseAuthoring(
        IStoredEmailSummaryReader summaryReader,
        IEmailContentStore contentStore,
        IEmailContentRenderer renderer,
        IEmailAttachmentContentReader attachmentContentReader,
        IEmailContentRepairRequestStore repairRequestStore,
        MailboxScopeResolver scopeResolver,
        IOutgoingSenderIdentityReader senderIdentities,
        NamedRecipientResolver recipientResolver,
        OutgoingEmailBounds bounds,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(summaryReader);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(attachmentContentReader);
        ArgumentNullException.ThrowIfNull(repairRequestStore);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(senderIdentities);
        ArgumentNullException.ThrowIfNull(recipientResolver);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(authorization);

        this.summaryReader = summaryReader;
        this.contentStore = contentStore;
        this.renderer = renderer;
        this.attachmentContentReader = attachmentContentReader;
        this.repairRequestStore = repairRequestStore;
        this.scopeResolver = scopeResolver;
        this.senderIdentities = senderIdentities;
        this.recipientResolver = recipientResolver;
        this.bounds = bounds;
        this.authorization = authorization;
    }

    /// <summary>Authors one answer to a stored email, or refuses it.</summary>
    /// <param name="request">What is being answered, how, and what its author wrote.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The authored answer, or the refusal that stopped it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>
    /// The grant asked for is the one that reads mail, because reading is what this does: the answer it produces quotes
    /// the message it answers and a forward carries that message's files, so anything reaching here without it would
    /// read mail by asking to reply to it. Whether the answer may then be sent is the sending path's question, asked
    /// where the send happens.
    /// </remarks>
    public async Task<AuthoredResponse> AuthorAsync(
        AuthoredResponseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        var summary = await this.summaryReader.FindAsync(request.AnsweredEmailId, cancellationToken);

        // An account this deployment no longer serves leaves its stored rows in place, and a folder an operator
        // withheld from tools keeps its own, so the row existing is not enough. All three cases produce one answer,
        // because telling them apart would let a caller learn which mail exists by trying to answer it.
        if (summary is null || !this.scopeResolver.IsReadableByTools(summary.AccountId, summary.FolderAlias))
        {
            return AuthoredResponse.Refused(AuthoredResponseRefusalReason.AnsweredEmailNotFound);
        }

        if (this.senderIdentities.FindSenderIdentity(summary.AccountId) is not { } sender)
        {
            return AuthoredResponse.Refused(AuthoredResponseRefusalReason.SenderUnconfigured);
        }

        if (summary.ContentAvailability is not StoredEmailContentAvailability.Available)
        {
            return AuthoredResponse.Refused(AuthoredResponseRefusalReason.AnsweredEmailContentUnavailable);
        }

        var content = await this.contentStore.FindStoredContentAsync(request.AnsweredEmailId, cancellationToken);
        if (content is null)
        {
            return await this.RefuseAndRequestRepairAsync(summary, EmailContentDefect.Missing, cancellationToken);
        }

        if (content.FindIntegrityDefect() is { } integrityDefect)
        {
            return await this.RefuseAndRequestRepairAsync(summary, integrityDefect, cancellationToken);
        }

        await this.repairRequestStore.NoteIfServedFromRetainedCopyAsync(
            content,
            summary.StoredEmailId,
            cancellationToken);

        var rendered = await this.renderer.RenderAsync(
            content,
            this.QuotationBounds(request),
            cancellationToken);

        if (rendered.Rendering is not { } rendering)
        {
            return await this.RefuseAndRequestRepairAsync(summary, EmailContentDefect.Unreadable, cancellationToken);
        }

        // A body inside a cryptographic envelope is held rather than damaged, so nothing is repaired and nothing is
        // fetched again. What it means for an answer is the same as content nobody holds: there is nothing to quote,
        // and quoting nothing produces an answer to an apparently empty message.
        if (rendering.BodyIsEncrypted)
        {
            return AuthoredResponse.Refused(AuthoredResponseRefusalReason.AnsweredEmailContentUnavailable);
        }

        return await this.AuthorFromAsync(request, summary, sender, content, rendering, cancellationToken);
    }

    /// <summary>Builds the answer from the message that was read for it.</summary>
    private async Task<AuthoredResponse> AuthorFromAsync(
        AuthoredResponseRequest request,
        EmailSummary summary,
        OutgoingSenderIdentity sender,
        StoredEmailContent content,
        EmailContentRendering rendering,
        CancellationToken cancellationToken)
    {
        if (this.RefuseAttachmentsBeyondBounds(request, rendering) is { } attachmentRefusal)
        {
            return attachmentRefusal;
        }

        // Before any attachment is read, because a recipient nobody can be found for refuses the answer whatever it
        // would have carried, and the book is one indexed lookup against a forward's worth of decoded files.
        var recipients = await this.recipientResolver.ResolveAsync(request.Recipients, cancellationToken);

        if (recipients.Refusal is { } recipientRefusal)
        {
            return AuthoredResponse.Refused(recipientRefusal);
        }

        IReadOnlyList<AuthoredEmailAttachment>? attachments = request.Act is AuthoredResponseAct.Forward
            ? await this.CarryAttachmentsAsync(content, rendering.Attachments, cancellationToken)
            : [];

        if (attachments is null)
        {
            return await this.RefuseAndRequestRepairAsync(summary, EmailContentDefect.Unreadable, cancellationToken);
        }

        var attribution = AnsweredEmailQuotation.Attribution(rendering.Headers, request.Act);

        var authored = new AuthoredEmail
        {
            // The sending address is the whole of what configuration states this account owns, so it is the whole of
            // what a reply to all leaves out. A mailbox the account is also reached at under an address its Delivery
            // block never names is not something anything here can know about.
            Recipients = AnsweredEmailRecipients.For(
                request.Act,
                rendering.Headers,
                new HashSet<EmailAddress> { sender.Address },
                recipients.Recipients),
            Subject = request.Act is AuthoredResponseAct.Forward
                ? ResponseSubject.ForForward(rendering.Headers.Subject)
                : ResponseSubject.ForReply(rendering.Headers.Subject),
            PlainTextBody = AnsweredEmailQuotation.PlainTextBody(
                request.PlainTextBody,
                attribution,
                rendering.PlainTextBody.Text,
                this.bounds.MaxBodyCharacters),
            HtmlBody = request.HtmlBody is { } htmlBody
                ? AnsweredEmailQuotation.HtmlBody(
                    htmlBody,
                    attribution,
                    rendering.SanitizedHtmlBody?.Text,
                    rendering.PlainTextBody.Text,
                    this.bounds.MaxBodyCharacters)
                : null,
            Attachments = attachments,
            Threading = OutgoingThreadPlacement.Answering(rendering.Headers.ThreadReferences),
        };

        return AuthoredResponse.Authored(summary.Account, authored);
    }

    /// <summary>States how much of the answered message may be quoted, which is what the composed body leaves room for.</summary>
    /// <remarks>
    /// <para>
    /// The bound is computed before the message is rendered rather than applied to what came back, because the markup
    /// representation is bounded on its source and sanitized again: cutting it afterwards would hand back an element
    /// somebody else opened and this system closed nowhere. Reducing the allowance instead means the rendering returns
    /// history that already fits beneath what the author wrote.
    /// </para>
    /// <para>
    /// Only the per-representation bound is set, and the read's budget is left unspent. That budget is the one a call
    /// naming several emails divides between them, and a rendering spends it on the plain text before the markup — so
    /// setting it to the same number as the bound beside it would leave the markup whatever an ordinary original's
    /// plain text did not take, which is next to nothing. One answer reads one message, so there is no call to divide
    /// anything between, and each representation is bounded on its own exactly as the two authored bodies are.
    /// </para>
    /// </remarks>
    private EmailContentRenderingBounds QuotationBounds(AuthoredResponseRequest request)
    {
        var authoredCharacters = Math.Max(request.PlainTextBody.Length, request.HtmlBody?.Length ?? 0);

        var allowance = Math.Max(
            0,
            this.bounds.MaxBodyCharacters - authoredCharacters - AnsweredEmailQuotation.QuotationOverheadReserve);

        return new EmailContentRenderingBounds(request.HtmlBody is not null, allowance, int.MaxValue);
    }

    /// <summary>Refuses a forward whose files this deployment does not compose, before any octet of one is read.</summary>
    /// <remarks>
    /// The bounds are the same ones any authored attachment set is held to, and they are checked against what the
    /// rendering measured rather than against what a header claimed. Checking first is what keeps a message with two
    /// hundred files from being decoded into memory to be refused afterwards.
    /// </remarks>
    private AuthoredResponse? RefuseAttachmentsBeyondBounds(
        AuthoredResponseRequest request,
        EmailContentRendering rendering)
    {
        if (request.Act is not AuthoredResponseAct.Forward || rendering.Attachments.Count == 0)
        {
            return null;
        }

        if (rendering.Attachments.Count > this.bounds.MaxAttachmentCount)
        {
            return AuthoredResponse.Refused(
                AuthoredResponseRefusalReason.BoundExceeded,
                this.bounds.MaxAttachmentCount);
        }

        if (rendering.Attachments.Any(attachment => attachment.DecodedSizeOctets > this.bounds.MaxAttachmentBytes))
        {
            return AuthoredResponse.Refused(
                AuthoredResponseRefusalReason.BoundExceeded,
                this.bounds.MaxAttachmentBytes);
        }

        return rendering.Attachments.Sum(static attachment => attachment.DecodedSizeOctets)
            > this.bounds.MaxMessageBytes
            ? AuthoredResponse.Refused(AuthoredResponseRefusalReason.BoundExceeded, this.bounds.MaxMessageBytes)
            : null;
    }

    /// <summary>Reads every file the forwarded message carries out of the stored copy.</summary>
    /// <returns>The files to attach, or <see langword="null" /> when the stored bytes stopped yielding one.</returns>
    /// <remarks>
    /// The octets are the original's own rather than anything derived from what was recorded about them, which is why a
    /// forward is worth beginning from a local copy: the alternative is a second fetch from the mail server, which a
    /// send has no business performing, or rebuilding files from their descriptions, which cannot be done.
    /// </remarks>
    private async Task<IReadOnlyList<AuthoredEmailAttachment>?> CarryAttachmentsAsync(
        StoredEmailContent content,
        IReadOnlyList<ExtractedEmailAttachment> descriptions,
        CancellationToken cancellationToken)
    {
        var carried = new List<AuthoredEmailAttachment>(descriptions.Count);

        for (var position = 0; position < descriptions.Count; position++)
        {
            var opened = await this.attachmentContentReader.OpenAsync(content, position, cancellationToken);

            if (opened.Attachment is not { } attachment)
            {
                return null;
            }

            await using (attachment)
            {
                using var octets = new MemoryStream();
                await attachment.WriteContentToAsync(octets, cancellationToken);

                carried.Add(new AuthoredEmailAttachment(
                    FileNameOf(attachment.Description, position),
                    attachment.Description.MediaType,
                    octets.ToArray()));
            }
        }

        return carried;
    }

    /// <summary>Names one carried file, and names an unnamed part after its position.</summary>
    /// <remarks>
    /// A part the sender left unnamed has no name to carry, and the composition refuses a file that declares none. The
    /// alternative to writing one is refusing to forward the message at all, which would make somebody else's omission
    /// the reason a forward is impossible — so this system names the part itself. That is not the same as recording a
    /// name as though a sender wrote it: this name exists only in the message being composed, where it is plainly this
    /// system's own.
    /// </remarks>
    private static string FileNameOf(ExtractedEmailAttachment description, int position) =>
        description.FileName is { } fileName
            ? fileName.Value
            : string.Create(CultureInfo.InvariantCulture, $"attachment-{position + 1}");

    /// <summary>Records the defect durably and refuses the answer.</summary>
    /// <remarks>
    /// The finding is about the stored copy rather than about what was being attempted with it, so it is recorded here
    /// exactly as reading the message's content records it. Discarding it because the request happened to be a reply
    /// would leave a damaged local copy discovered and unrecorded.
    /// </remarks>
    private async Task<AuthoredResponse> RefuseAndRequestRepairAsync(
        EmailSummary summary,
        EmailContentDefect defect,
        CancellationToken cancellationToken)
    {
        await this.repairRequestStore.RecordAsync(
            new EmailContentRepairRequest(summary.StoredEmailId, defect),
            cancellationToken);

        return AuthoredResponse.Refused(AuthoredResponseRefusalReason.AnsweredEmailContentUnavailable);
    }
}

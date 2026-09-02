// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery.Drafts;

namespace MailFathom.Application.Mail.Delivery.Drafts;

/// <summary>Takes files onto a draft and back off it, which is how an author attaches anything at all.</summary>
/// <remarks>
/// <para>
/// A file is staged against the draft rather than sent with each edit. Composing the message is what puts the octets
/// into it, and an author revising a subject would otherwise re-upload every file they had attached; so the upload
/// happens once, the draft keeps what was uploaded, and every later revision is composed with it.
/// </para>
/// <para>
/// <b>Cancelling an upload is one of two things, and both leave nothing behind.</b> A request the author abandoned
/// mid-transfer commits nothing at all, because the row and the octets are one transaction that never ran; a file
/// already taken in is removed by naming it, and its octets go with the row. Neither leaves a payload this deployment
/// holds for a message nobody is writing.
/// </para>
/// <para>
/// The bounds are the operator's numbers rather than the transport's, and they are asked here so that every boundary
/// that ever stages a file meets them: how many files a message may carry and how large one may be are what
/// <see cref="OutgoingEmailBounds" /> already states for a composed message, and a file this refuses is one no
/// composition of that draft could have carried anyway. What the composed size turns out to be is asked again at
/// composition, because transfer encoding is what decides it.
/// </para>
/// <para>
/// Every act here is admitted under <see cref="MailFathomPermission.MailDraftsWrite" />, because attaching a file is
/// writing the draft. Which draft the caller may reach is <see cref="MailDraftDirectory" />'s answer rather than this
/// one's, for the reason that class gives.
/// </para>
/// </remarks>
/// <param name="directory">Resolves an identifier into a draft the caller's own owner holds.</param>
/// <param name="drafts">Holds the durable account of every draft and everything staged against one.</param>
/// <param name="retryPolicy">Commits the row and the octets together.</param>
/// <param name="bounds">States how many files a message may carry and how large one may be.</param>
/// <param name="authorization">Answers whether the caller that reached this holds the grant that lets it draft.</param>
/// <param name="timeProvider">Stamps when the upload was taken in.</param>
public sealed class MailDraftAttachments(
    MailDraftDirectory directory,
    IMailDraftStore drafts,
    OptimisticConcurrencyRetryPolicy retryPolicy,
    OutgoingEmailBounds bounds,
    AccessAuthorization authorization,
    TimeProvider timeProvider)
{
    /// <summary>Stages one file against a draft the caller's owner is writing.</summary>
    /// <param name="draftId">The draft the file is attached to.</param>
    /// <param name="file">What the file is called, what it declares itself to be, and the octets it is made of.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns>The staged file as the write left it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="file" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.MailDraftsWrite" />, or is acting for no owner.</exception>
    /// <exception cref="MailDraftRefusedException">Thrown when the caller's owner holds no draft still being written under that identifier, when the draft already carries as many files as a message may, or when the file is larger than one this deployment composes.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when the write did not commit on any allowed attempt.</exception>
    /// <remarks>
    /// The draft's stored message is left exactly as it was: a staged file joins the message where the next revision
    /// is composed, so uploading a file and saving the draft are two acts and the second is the one that changes what
    /// the message is. That is what keeps a large file from being re-composed and re-appended on the strength of an
    /// upload the author may still take back.
    /// </remarks>
    public async Task<MailDraftAttachment> StageAsync(
        MailDraftId draftId,
        AuthoredEmailAttachment file,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        authorization.RequirePermission(MailFathomPermission.MailDraftsWrite);

        var draft = await this.RequireWritableAsync(draftId, cancellationToken);

        // Judged before anything is written, because both values are the author's own words and both end up in a
        // header. What a header cannot carry at all is the composition's answer; what a column cannot carry is this.
        if (string.IsNullOrWhiteSpace(file.FileName)
            || file.FileName.Length > MailDraftAttachment.MaximumFileNameLength
            || string.IsNullOrWhiteSpace(file.MediaType)
            || file.MediaType.Length > MailDraftAttachment.MaximumMediaTypeLength)
        {
            throw MailDraftRefusedException.From(new AuthoredEmailRefusal(
                AuthoredEmailRefusalReason.FieldUnusable,
                AuthoredEmailField.Attachment));
        }

        if (draft.Attachments.Count >= bounds.MaxAttachmentCount)
        {
            throw MailDraftRefusedException.From(new AuthoredEmailRefusal(
                AuthoredEmailRefusalReason.BoundExceeded,
                AuthoredEmailField.Attachment,
                bounds.MaxAttachmentCount));
        }

        if (file.Content.Length > bounds.MaxAttachmentBytes)
        {
            throw MailDraftRefusedException.From(new AuthoredEmailRefusal(
                AuthoredEmailRefusalReason.BoundExceeded,
                AuthoredEmailField.Attachment,
                bounds.MaxAttachmentBytes));
        }

        var stagedAt = timeProvider.GetUtcNow();

        return await retryPolicy.CommitAsync(
            async (session, token) =>
            {
                // Counted again inside the commit rather than only above it. The check above answers the ordinary
                // case and answers it before a large body is read; this is what a second upload that raced past it
                // meets. Staging moves the draft's own row, so the loser of that race conflicts and is retried, and
                // the attempt that follows counts what the winner left rather than what both of them saw.
                var held = await drafts.FindAsync(draftId, token) ?? throw MailDraftRefusedException.NotFound();

                if (held.Attachments.Count >= bounds.MaxAttachmentCount)
                {
                    throw MailDraftRefusedException.From(new AuthoredEmailRefusal(
                        AuthoredEmailRefusalReason.BoundExceeded,
                        AuthoredEmailField.Attachment,
                        bounds.MaxAttachmentCount));
                }

                return await drafts.StageAttachmentAsync(session, draftId, file, stagedAt, token);
            },
            cancellationToken);
    }

    /// <summary>Takes one staged file back off a draft the caller's owner is writing.</summary>
    /// <param name="draftId">The draft the file was attached to.</param>
    /// <param name="attachmentId">The file to take off.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns><see langword="true" /> when a file was taken off; <see langword="false" /> when the draft carries none under that identifier.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.MailDraftsWrite" />, or is acting for no owner.</exception>
    /// <exception cref="MailDraftRefusedException">Thrown when the caller's owner holds no draft still being written under that identifier.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when the write did not commit on any allowed attempt.</exception>
    /// <remarks>
    /// Taking a file off twice is one removal, and the second answers that the draft carries no such file rather than
    /// failing. The message the draft is stored as still carries the file until the next revision is composed, for the
    /// reason staging one does not put it there.
    /// </remarks>
    public async Task<bool> UnstageAsync(
        MailDraftId draftId,
        MailDraftAttachmentId attachmentId,
        CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailDraftsWrite);

        await this.RequireWritableAsync(draftId, cancellationToken);

        return await retryPolicy.CommitAsync(
            (session, token) => drafts.UnstageAttachmentAsync(session, draftId, attachmentId, token),
            cancellationToken);
    }

    /// <summary>Requires that the identifier names a draft this caller's owner is still writing.</summary>
    /// <remarks>
    /// A draft of another owner, one already given up, one already promoted, and one nobody holds are one refusal, for
    /// the reason revising a draft gives: telling them apart would let a caller learn which drafts exist by attaching
    /// a file to them.
    /// </remarks>
    private async Task<MailDraftRecord> RequireWritableAsync(
        MailDraftId draftId,
        CancellationToken cancellationToken) =>
        await directory.FindAsync(draftId, cancellationToken)
            is { IsDiscarded: false, PromotedTo: null } draft
            ? draft
            : throw MailDraftRefusedException.NotFound();
}

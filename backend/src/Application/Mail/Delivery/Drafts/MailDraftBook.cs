// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Screening;
using MailFathom.Application.Persistence;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;

namespace MailFathom.Application.Mail.Delivery.Drafts;

/// <summary>Holds every draft this deployment keeps, and is the one way one is written, revised, or given up.</summary>
/// <remarks>
/// <para>
/// It is the drafts' counterpart of <see cref="MailOutbox" /> and exists for the same reason: the record and the
/// message are one decision. A draft whose message was never stored describes a revision nothing can append or promote,
/// and a message stored under no draft is bytes nothing will ever read — so both cross one transaction here, and a
/// crash between them leaves neither rather than half of a draft.
/// </para>
/// <para>
/// Being the one way in is also what makes it the place the grant is asked for. A draft is written into the owner's own
/// mailbox and reaches nobody else, so it is admitted under the grant that says exactly that: a caller holding
/// <see cref="MailFathomPermission.MailDraftsWrite" /> for a command, and MailFathom's own identity for a rule. That is
/// asked with no transport in the picture, so a second entrypoint added later meets it whatever it did first. What it
/// deliberately is not is <see cref="MailFathomPermission.MailSend" />: a caller that may draft and may not send is the
/// arrangement the two grants exist to make possible, and what stands between a draft and a recipient is the promotion,
/// which asks for the sending grant on its own.
/// </para>
/// <para>
/// The mailbox is brought into step once the write has committed, and never before it. A crash in between leaves a
/// draft the pass will settle; a crash the other way round would leave a message in somebody's drafts folder that
/// nothing here can name.
/// </para>
/// </remarks>
public sealed class MailDraftBook
{
    private readonly IMailDraftStore drafts;
    private readonly IEmailContentStore contentStore;
    private readonly OptimisticConcurrencyRetryPolicy retryPolicy;
    private readonly MailDraftFiler filer;
    private readonly OutgoingMailScreening screening;
    private readonly AccessAuthorization authorization;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the book from the record it writes and the mailbox it keeps in step.</summary>
    /// <param name="drafts">Holds the durable account of every draft.</param>
    /// <param name="contentStore">Holds the composed MIME each revision is.</param>
    /// <param name="retryPolicy">Commits the record and the message together.</param>
    /// <param name="filer">Brings the drafts folder into step with what was written.</param>
    /// <param name="screening">Answers whether what the draft says is something this deployment lets onto a mail server.</param>
    /// <param name="authorization">Answers whether whoever reached this is admitted to write a draft at all.</param>
    /// <param name="timeProvider">Stamps every instant the record carries.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public MailDraftBook(
        IMailDraftStore drafts,
        IEmailContentStore contentStore,
        OptimisticConcurrencyRetryPolicy retryPolicy,
        MailDraftFiler filer,
        OutgoingMailScreening screening,
        AccessAuthorization authorization,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(retryPolicy);
        ArgumentNullException.ThrowIfNull(filer);
        ArgumentNullException.ThrowIfNull(screening);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.drafts = drafts;
        this.contentStore = contentStore;
        this.retryPolicy = retryPolicy;
        this.filer = filer;
        this.screening = screening;
        this.authorization = authorization;
        this.timeProvider = timeProvider;
    }

    /// <summary>Writes a composed draft down, as a new one or as the next revision of one that already exists.</summary>
    /// <param name="accountId">The account the draft belongs to.</param>
    /// <param name="author">The authored act writing it down.</param>
    /// <param name="composed">The composed message, its recipients, and the identity this revision carries.</param>
    /// <param name="revises">The draft this replaces, or <see langword="null" /> to write a new one.</param>
    /// <param name="cancellationToken">Cancels the writes and the commands that follow them.</param>
    /// <returns>The draft as it stands once the mailbox has been brought into step with it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="author" /> or <paramref name="composed" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the act the author names is not what reached this.</exception>
    /// <exception cref="MailDraftRefusedException">Thrown when <paramref name="revises" /> names no draft of this account that is still being written, or when the message carries material this deployment screens outgoing mail for.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the message carries, which refuses the draft rather than filing it unscreened.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when the write did not commit on any allowed attempt.</exception>
    /// <remarks>
    /// The revision is durable before any command reaches the mail server, which is what makes a replacement
    /// resumable: the record already says which copy is being replaced, so a process that dies between the append and
    /// the removal leaves one draft in the folder rather than two or none.
    /// </remarks>
    public async Task<MailDraftRecord> SaveAsync(
        MailAccountId accountId,
        OutgoingEmailRequester author,
        ComposedMailDraft composed,
        MailDraftId? revises,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(composed);

        this.RequireAdmittedToDraft(author.Origin);

        if (revises is { } revisedDraftId)
        {
            await this.RequireRevisableAsync(accountId, revisedDraftId, cancellationToken);
        }

        // Before the write, so a refused draft leaves neither a record nor a message nor a copy in the mailbox — and
        // asked on every revision rather than on the first one alone, because a revision is a new message and because a
        // draft written before the screen was switched on would otherwise carry its way past it one edit at a time.
        if (await this.screening.FindRefusalAsync(composed.RawMime, cancellationToken) is { } screened)
        {
            throw MailDraftRefusedException.ContentRefused(screened);
        }

        var writtenAt = this.timeProvider.GetUtcNow();

        // Before the unit of work rather than inside it. Every revision is placed under a key of its own, so a commit
        // that never happens leaves the row pointing at the previous revision's object, which is intact.
        var placedContent = await this.contentStore.PlaceContentAsync(
            EmailContentKind.MailDraft,
            composed.RawMime,
            cancellationToken);

        var draft = await this.retryPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                var written = revises is { } draftId
                    ? await this.drafts.ReviseAsync(
                        session,
                        draftId,
                        composed.Recipients,
                        composed.RawMime.Length,
                        writtenAt,
                        attemptCancellationToken)
                    : await this.drafts.OpenAsync(
                        session,
                        accountId,
                        author,
                        composed.Recipients,
                        composed.RawMime.Length,
                        writtenAt,
                        attemptCancellationToken);

                await this.contentStore.SaveMailDraftContentAsync(
                    session,
                    written.Id,
                    placedContent,
                    attemptCancellationToken);

                return written;
            },
            cancellationToken);

        await this.filer.SettleAsync(draft, cancellationToken);

        return await this.drafts.FindAsync(draft.Id, cancellationToken) ?? draft;
    }

    /// <summary>Gives up one draft and takes the copies of it back out of the mailbox.</summary>
    /// <param name="draftId">The draft to give up.</param>
    /// <param name="cancellationToken">Cancels the write and the commands that follow it.</param>
    /// <returns>What settling the mailbox did, which is already durable by the time it is returned.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.MailDraftsWrite" />.</exception>
    /// <exception cref="MailDraftRefusedException">Thrown when no draft this deployment holds is still one to give up under that identifier.</exception>
    /// <remarks>
    /// <para>
    /// <b>Only a draft this system created can be given up here.</b> The identifier names a record MailFathom wrote, and
    /// the copies that record names are the only occurrences the removal ever reaches — so a draft the owner wrote in
    /// their own mail client is not refused by a check, it is unreachable, because nothing holds it under an identifier
    /// this method accepts.
    /// </para>
    /// <para>
    /// The record is marked before anything is issued and removed once the copies are settled, so a process that dies
    /// in between leaves a draft the pass finishes rather than a message in the owner's folder that nothing can name.
    /// </para>
    /// <para>
    /// <b>A promoted draft is refused rather than given up here.</b> Its message is a queued send that this would leave
    /// untouched, so removing the draft would answer a caller asking for the message not to exist by sending it anyway
    /// and keeping no record of where it came from. What stops such a send is cancelling the send, and until it is
    /// delivered or cancelled the draft stands — which is the same answer revising a promoted draft already gives.
    /// </para>
    /// </remarks>
    public async Task<MailDraftFilingResult> DiscardAsync(
        MailDraftId draftId,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailDraftsWrite);

        if (await this.drafts.FindAsync(draftId, cancellationToken) is not { PromotedTo: null } draft)
        {
            throw MailDraftRefusedException.NotFound();
        }

        if (!draft.IsDiscarded)
        {
            await this.retryPolicy.CommitAsync(
                (session, token) => this.drafts.RecordDiscardedAsync(
                    session,
                    draftId,
                    this.timeProvider.GetUtcNow(),
                    token),
                cancellationToken);
        }

        var discarded = await this.drafts.FindAsync(draftId, cancellationToken) ?? draft;

        return await this.filer.SettleAsync(discarded, cancellationToken);
    }

    /// <summary>Requires that the draft a revision names is one of this account's that is still being written.</summary>
    /// <remarks>
    /// The three refusals are one answer on purpose. A draft of another account, a draft already given up, and a draft
    /// nobody holds are all a caller naming something it may not revise, and telling them apart would let it learn
    /// which drafts exist by asking to revise them.
    /// </remarks>
    private async Task RequireRevisableAsync(
        MailAccountId accountId,
        MailDraftId draftId,
        CancellationToken cancellationToken)
    {
        if (await this.drafts.FindAsync(draftId, cancellationToken)
            is not { IsDiscarded: false, PromotedTo: null } draft
            || draft.AccountId != accountId)
        {
            throw MailDraftRefusedException.NotFound();
        }
    }

    /// <summary>Requires that whatever reached this book is the kind of act the draft says wrote it.</summary>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when it is not.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the origin is one this method was never taught, which is a defect here rather than a refusal.</exception>
    /// <remarks>
    /// It admits the same two principals the outbox does and asks a different permission of the first, because what a
    /// draft is and what a send is differ in exactly that: nothing here reaches anybody but the mailbox's own owner. A
    /// rule is admitted on MailFathom's own identity as it is at the outbox, since an act nobody is present for carries
    /// no caller's grant either way.
    /// </remarks>
    private void RequireAdmittedToDraft(OutgoingEmailOrigin origin)
    {
        switch (origin)
        {
            case OutgoingEmailOrigin.Command:
                this.authorization.RequirePermission(MailFathomPermission.MailDraftsWrite);

                break;

            case OutgoingEmailOrigin.Rule:
                this.authorization.RequireProcessIdentity();

                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(origin),
                    origin,
                    "The outgoing email origin names no act this draft book admits.");
        }
    }
}

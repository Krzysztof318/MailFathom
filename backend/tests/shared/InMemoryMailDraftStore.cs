// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.TestSupport;

/// <summary>Holds drafts and their copies in memory, with the movements the durable store makes and no others.</summary>
/// <remarks>
/// The records are immutable and every write replaces one, so a record a test read earlier goes on saying what it said
/// — which is exactly how a caller holding a record it read before a mail server answered behaves against the real
/// store, and is what makes a resumed-after-a-crash test express a crash by simply not calling the next write.
/// </remarks>
internal sealed class InMemoryMailDraftStore : IMailDraftStore
{
    private readonly Dictionary<MailDraftId, MailDraftRecord> drafts = [];
    private readonly Dictionary<MailDraftAttachmentId, AuthoredEmailAttachment> staged = [];

    /// <summary>Gets every draft still held, which is what a deletion is asserted against.</summary>
    internal IReadOnlyCollection<MailDraftRecord> Drafts => [.. this.drafts.Values];

    /// <summary>Reads one draft as it now stands, without going through the port.</summary>
    internal MailDraftRecord? Peek(MailDraftId draftId) =>
        this.drafts.TryGetValue(draftId, out var draft) ? draft : null;

    /// <summary>States a draft as a second caller reaching it before the first caller's promotion committed reads it.</summary>
    /// <param name="draftId">The draft that promotion is about.</param>
    /// <remarks>
    /// It arranges the one state a sequential test cannot reach: two promotions of one draft that both find nothing
    /// promoted, because neither read can see a write that has not been committed yet. What settles that is the
    /// request's own identity rather than this read, and this is how a test reaches the case that proves it.
    /// </remarks>
    internal void ForgetPromotion(MailDraftId draftId) =>
        this.drafts[draftId] = this.Require(draftId) with { PromotedTo = null };

    /// <inheritdoc />
    public Task<MailDraftRecord> OpenAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        OutgoingEmailRequester author,
        IReadOnlyList<MailDraftRecipient> recipients,
        string subject,
        long mimeByteLength,
        DateTimeOffset composedAt,
        CancellationToken cancellationToken)
    {
        var draft = new MailDraftRecord
        {
            Id = MailDraftId.Create(Guid.CreateVersion7(composedAt)),
            Account = account,
            Author = author,
            Recipients = [.. recipients],
            Subject = subject,
            Attachments = [],
            MimeByteLength = mimeByteLength,
            Revision = 1,
            ComposedAt = composedAt,
            RevisedAt = composedAt,
            DiscardedAt = null,
            PromotedTo = null,
            Copies = [],
            Divergence = null,
            LastFailure = null,
        };

        this.drafts[draft.Id] = draft;

        return Task.FromResult(draft);
    }

    /// <inheritdoc />
    public Task<MailDraftRecord> ReviseAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        IReadOnlyList<MailDraftRecipient> recipients,
        string subject,
        long mimeByteLength,
        DateTimeOffset revisedAt,
        CancellationToken cancellationToken)
    {
        var draft = this.Require(draftId);

        // Asked the way the real store asks it: a draft that has been given up is not revised, so a test reaching that
        // state through this double meets the refusal production would give rather than a silent new revision.
        if (draft.IsDiscarded)
        {
            throw new InvalidOperationException($"Mail draft {draftId} has been given up, so nothing revises it.");
        }

        var revised = draft with
        {
            Recipients = [.. recipients],
            Subject = subject,
            MimeByteLength = mimeByteLength,
            RevisedAt = revisedAt,
        };

        revised = revised with { Revision = revised.Revision + 1 };
        this.drafts[draftId] = revised;

        return Task.FromResult(revised);
    }

    /// <inheritdoc />
    public Task<MailDraftRecord?> FindAsync(MailDraftId draftId, CancellationToken cancellationToken) =>
        Task.FromResult(this.Peek(draftId));

    /// <inheritdoc />
    public Task<IReadOnlyList<MailDraftRecord>> ReadForOwnerAsync(
        MailOwnerId owner,
        MailAccountId? account,
        int maxCount,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MailDraftRecord>>(
        [
            .. this.drafts.Values
                .Where(draft => draft.Account.Owner == owner)
                .Where(draft => account is not { } narrowed || draft.AccountId == narrowed)
                .Where(draft => !draft.IsDiscarded && draft.PromotedTo is null)
                .OrderByDescending(draft => draft.RevisedAt)
                .ThenBy(draft => draft.Id.Value)
                .Take(maxCount),
        ]);

    /// <inheritdoc />
    /// <remarks>
    /// A draft nobody holds answers with nothing rather than refusing, which is what the real store does: the query
    /// reads the file rows of one draft, and there are none. Refusing here instead would make a caller that reads the
    /// files before it establishes the draft exists fail in this double and pass against PostgreSQL.
    /// </remarks>
    public Task<IReadOnlyList<AuthoredEmailAttachment>> ReadAttachmentContentAsync(
        MailDraftId draftId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AuthoredEmailAttachment>>(
            this.drafts.TryGetValue(draftId, out var draft)
                ? [.. draft.Attachments.Select(attachment => this.staged[attachment.Id])]
                : []);

    /// <inheritdoc />
    public Task<MailDraftAttachment> StageAttachmentAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        AuthoredEmailAttachment file,
        DateTimeOffset stagedAt,
        CancellationToken cancellationToken)
    {
        var draft = this.Require(draftId);

        var attachment = new MailDraftAttachment(
            MailDraftAttachmentId.Create(Guid.CreateVersion7(stagedAt)),
            file.FileName,
            file.MediaType,
            file.Content.Length,
            stagedAt);

        this.staged[attachment.Id] = file;

        // Moved with the file, matching the real store: staging takes part in the draft's own row so that two uploads
        // racing each other conflict, and a listing ordered by the last change has to see an attached file as one.
        this.drafts[draftId] = draft with
        {
            Attachments = [.. draft.Attachments, attachment],
            RevisedAt = stagedAt,
        };

        return Task.FromResult(attachment);
    }

    /// <inheritdoc />
    public Task<bool> UnstageAttachmentAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        MailDraftAttachmentId attachmentId,
        CancellationToken cancellationToken)
    {
        var draft = this.Require(draftId);

        if (draft.Attachments.All(attachment => attachment.Id != attachmentId))
        {
            return Task.FromResult(false);
        }

        this.staged.Remove(attachmentId);
        this.drafts[draftId] = draft with
        {
            Attachments = [.. draft.Attachments.Where(attachment => attachment.Id != attachmentId)],
        };

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<MailDraftRecord?> FindPromotedToAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.drafts.Values.FirstOrDefault(draft => draft.PromotedTo == outgoingEmailId));

    /// <inheritdoc />
    public Task<IReadOnlyList<MailDraftRecord>> ReadOutstandingAsync(
        MailAccountIdentity account,
        int maxCount,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MailDraftRecord>>(
        [
            .. this.drafts.Values
                .Where(draft => draft.Account == account && draft.HasOutstandingServerWork)
                .OrderBy(draft => draft.RevisedAt)
                .ThenBy(draft => draft.Id.Value)
                .Take(maxCount),
        ]);

    /// <inheritdoc />
    public Task RecordAppendIssuedAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        MailFolderResolution destination,
        DateTimeOffset appendedAt,
        CancellationToken cancellationToken)
    {
        var draft = this.Require(draftId);

        if (draft.FindCopy(draft.Revision) is not null)
        {
            throw new InvalidOperationException("That revision has already been appended.");
        }

        this.drafts[draftId] = draft with
        {
            Copies =
            [
                new MailDraftServerCopy
                {
                    Revision = draft.Revision,
                    FolderAlias = destination.Alias,
                    FolderPath = destination.RemotePath,
                    Stage = MailDraftCopyStage.Issued,
                    Placement = RemoteEmailPlacement.NotReported(),
                    InternetMessageId = null,
                    AppendedAt = appendedAt,
                    SettledAt = null,
                },
                .. draft.Copies,
            ],
        };

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordAppendConfirmedAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        AppendedMailCopy copy,
        CancellationToken cancellationToken)
    {
        var draft = this.Require(draftId);

        // Asked the way the real store asks it: a confirmation follows an issued append and nothing else, so a double
        // confirmation and a confirmation of a copy already standing are refused here rather than quietly accepted by
        // a double the production write would have stopped.
        if (draft.FindCopy(draft.Revision) is not { Stage: MailDraftCopyStage.Issued })
        {
            throw new InvalidOperationException(
                $"Revision {draft.Revision} of mail draft {draftId} has no copy awaiting confirmation.");
        }

        this.Replace(
            draft,
            draft.Revision,
            appended => appended with
            {
                Stage = MailDraftCopyStage.Standing,
                Placement = copy.Placement,
                InternetMessageId = copy.InternetMessageId,
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordCopySettledAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        int revision,
        MailDraftCopyStage stage,
        DateTimeOffset settledAt,
        CancellationToken cancellationToken)
    {
        // The same closed pair the real store accepts. A copy is settled as withdrawn or as abandoned and as nothing
        // else, so a caller writing any other stage fails here rather than only against PostgreSQL.
        if (stage is not (MailDraftCopyStage.Withdrawn or MailDraftCopyStage.Abandoned))
        {
            throw new ArgumentOutOfRangeException(
                nameof(stage),
                stage,
                "A copy of a draft is settled as withdrawn or as abandoned, and as nothing else.");
        }

        this.Replace(
            this.Require(draftId),
            revision,
            copy => copy with { Stage = stage, SettledAt = copy.SettledAt ?? settledAt });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordDiscardedAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        DateTimeOffset discardedAt,
        CancellationToken cancellationToken)
    {
        var draft = this.Require(draftId);

        this.drafts[draftId] = draft with { DiscardedAt = draft.DiscardedAt ?? discardedAt };

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordPromotedAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var draft = this.Require(draftId);

        this.drafts[draftId] = draft with { PromotedTo = draft.PromotedTo ?? outgoingEmailId };

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(IPersistenceSession session, MailDraftId draftId, CancellationToken cancellationToken)
    {
        this.drafts.Remove(draftId);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordDivergenceAsync(
        MailDraftId draftId,
        MailDraftDivergenceReason reason,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        if (this.drafts.TryGetValue(draftId, out var draft))
        {
            this.drafts[draftId] = draft with { Divergence = new MailDraftDivergence(reason, observedAt) };
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordFailureAsync(
        MailDraftId draftId,
        MailFathomErrorCode failure,
        CancellationToken cancellationToken)
    {
        if (this.drafts.TryGetValue(draftId, out var draft))
        {
            this.drafts[draftId] = draft with { LastFailure = failure };
        }

        return Task.CompletedTask;
    }

    private void Replace(
        MailDraftRecord draft,
        int revision,
        Func<MailDraftServerCopy, MailDraftServerCopy> change)
    {
        if (draft.FindCopy(revision) is null)
        {
            throw new InvalidOperationException($"No copy of revision {revision} is held.");
        }

        this.drafts[draft.Id] = draft with
        {
            Copies = [.. draft.Copies.Select(copy => copy.Revision == revision ? change(copy) : copy)],
        };
    }

    private MailDraftRecord Require(MailDraftId draftId) =>
        this.Peek(draftId) ?? throw new InvalidOperationException($"No draft is held under {draftId}.");
}

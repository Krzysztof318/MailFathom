// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One message this deployment holds that has not been sent and may never be.</summary>
/// <remarks>
/// It is a table of its own rather than a stage of the outgoing record, because a draft has no delivery, no recipient
/// that has to resolve, no idempotency identity, and no terminal stage — and it has the one thing an outgoing record
/// never does, a copy on somebody else's server that a revision has to replace.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailDraftEntity
{
    public Guid Id { get; set; }

    /// <summary>Gets or sets the account the draft belongs to and a promotion would send it as.</summary>
    /// <remarks>
    /// A plain column rather than a foreign key onto the stored account, for the reason the outgoing record's copy is
    /// one: an account configured to send need never have synchronized anything, so a key here would refuse a draft
    /// from a submission-only account instead of recording it.
    /// </remarks>
    public required string MailboxAccountId { get; set; }

    /// <summary>Gets or sets the owner whose account the draft is authored from.</summary>
    public required Guid OwnerId { get; set; }

    public OutgoingEmailOrigin RequesterOrigin { get; set; }

    public required string RequesterIdentity { get; set; }

    /// <summary>Gets or sets which revision of the draft the stored message is, counted from one.</summary>
    /// <remarks>
    /// It is what joins the stored message to the copy of it in the folder, so the copy carrying this number is the
    /// current one and every other standing copy is one a revision replaced.
    /// </remarks>
    public int Revision { get; set; }

    /// <summary>Gets or sets how many bytes of MIME are stored for the current revision.</summary>
    /// <remarks>
    /// Kept here as well as on the content row for the reason the outgoing record keeps its own: a promotion compares
    /// it against what this deployment sends before anything is read.
    /// </remarks>
    public long MimeByteLength { get; set; }

    public DateTimeOffset ComposedAt { get; set; }

    public DateTimeOffset RevisedAt { get; set; }

    /// <summary>Gets or sets when the draft was given up, and <see langword="null" /> while it stands.</summary>
    /// <remarks>
    /// The row outlives the decision on purpose: it is the only thing naming the copies that still have to be taken
    /// out of the folder, and it is removed once they are.
    /// </remarks>
    public DateTimeOffset? DiscardedAt { get; set; }

    /// <summary>Gets or sets the outgoing record a promotion wrote, and <see langword="null" /> while none has.</summary>
    /// <remarks>
    /// No foreign key stands behind it, deliberately. The send is erased on its own retention terms and the draft on
    /// its own, and neither erasure may take the other with it or be refused because of it.
    /// </remarks>
    public Guid? PromotedToOutgoingEmailId { get; set; }

    /// <summary>Gets or sets why the tracked copy stopped being one MailFathom may touch, and <see langword="null" /> while none has.</summary>
    public MailDraftDivergenceReason? DivergenceReason { get; set; }

    /// <summary>Gets or sets when the divergence was found out, and <see langword="null" /> while there has been none.</summary>
    public DateTimeOffset? DivergenceObservedAt { get; set; }

    /// <summary>Gets or sets the code of the failure the last attempt on the mailbox ended in, and <see langword="null" /> while none has.</summary>
    /// <remarks>Only the code is kept, for the reason the outgoing record keeps only its own: a message is text
    /// assembled at the failure site and may repeat what a remote server wrote.</remarks>
    public int? LastFailureCode { get; set; }

    public ICollection<MailDraftRecipientEntity> Recipients { get; } = [];

    /// <summary>Gets every copy of this draft MailFathom has appended, including the ones a revision replaced.</summary>
    public ICollection<MailDraftCopyEntity> Copies { get; } = [];

    /// <summary>Gets or sets the stored MIME of the current revision, loaded only where a caller asked for it.</summary>
    public MailDraftContentEntity? Content { get; set; }

    /// <summary>Gets or sets the PostgreSQL <c>xmin</c> token this row's optimistic concurrency is detected through.</summary>
    /// <remarks>See the stored-email mapping: this is the system column, not a user-defined one.</remarks>
    public uint ConcurrencyVersion { get; set; }
}

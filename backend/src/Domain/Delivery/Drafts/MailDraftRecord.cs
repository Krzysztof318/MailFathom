// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Failures;

namespace MailFathom.Domain.Delivery.Drafts;

/// <summary>Reports one message this deployment holds that has not been sent and may never be.</summary>
/// <remarks>
/// <para>
/// A draft is its own record rather than a stage of <see cref="OutgoingEmailRecord" />, because almost nothing an
/// outgoing record exists for is true of one. It has no delivery, no recipient that has to be valid, no idempotency
/// identity against a duplicate that could not be withdrawn, and no terminal stage — a draft is edited for as long as
/// its owner keeps editing it. What it has instead is the one thing an outgoing record never does: a copy on somebody
/// else's server that has to be replaced whenever the local one changes.
/// </para>
/// <para>
/// That replacement is the whole difficulty. IMAP has no command that changes a stored message, so a revision is an
/// <c>APPEND</c> of the new version followed by a removal of the old one, and a process can die between them. The
/// revision is therefore written down before the first command, and <see cref="Stage" /> is what a resumed attempt
/// reads to finish the pair instead of starting it again.
/// </para>
/// <para>
/// It is derived personal data on the same terms as the mail beside it: a draft says who this mailbox's owner is
/// writing to and what about. The addresses are here because a promotion cannot build an envelope without them, the
/// message itself stays in the content store, and the record is erased with the mail it belongs to.
/// </para>
/// </remarks>
public sealed record MailDraftRecord
{
    /// <summary>Gets what everything after the first write refers to this draft by, including its stored MIME.</summary>
    public required MailDraftId Id { get; init; }

    /// <summary>Gets the account the draft belongs to, and the one a promotion would send it as.</summary>
    public required MailAccountId AccountId { get; init; }

    /// <summary>Gets the authored act that wrote the draft down.</summary>
    /// <remarks>
    /// It is the same shape an outgoing record's requester is, so a draft written by a rule and one written by somebody
    /// present are told apart the same way a send is. It is provenance here rather than an idempotency identity: two
    /// identical requests to save a draft are two drafts, because a draft that turned out to exist twice costs an owner
    /// a deletion rather than a recipient a second message.
    /// </remarks>
    public required OutgoingEmailRequester Author { get; init; }

    /// <summary>Gets the people the draft is addressed to, which may be nobody.</summary>
    /// <remarks>
    /// <para>
    /// A draft addressed to nobody is an ordinary draft — writing the message before deciding who reads it is what a
    /// draft is for — and it is a draft nothing can promote, which is where the absence is refused rather than here.
    /// </para>
    /// <para>
    /// Each of them says where its address came from as well as what it is, which an outgoing record has no reason to.
    /// A promotion is the first moment a draft's recipients are governed at all, and the question that governance asks
    /// is whether the caller named the address itself.
    /// </para>
    /// </remarks>
    public required IReadOnlyList<MailDraftRecipient> Recipients { get; init; }

    /// <summary>Gets how many bytes of MIME are stored for the current revision.</summary>
    /// <remarks>
    /// It is kept beside the record for the reason an outgoing record keeps its own: a promotion compares it against
    /// what this deployment sends before anything is read, and reading the <c>bytea</c> to learn its length would load
    /// the message to answer a question about its size.
    /// </remarks>
    public required long MimeByteLength { get; init; }

    /// <summary>Gets which revision of the draft the stored message is, counted from one and increased by every edit.</summary>
    /// <remarks>
    /// It is what joins a stored message to the copy of it in the mailbox: <see cref="CurrentCopy" /> is the copy
    /// carrying this number and every other standing copy is one a revision replaced.
    /// </remarks>
    public required int Revision { get; init; }

    /// <summary>Gets when the draft was first written down.</summary>
    public required DateTimeOffset ComposedAt { get; init; }

    /// <summary>Gets when the draft last changed, which is what an owner sorts their drafts by.</summary>
    public required DateTimeOffset RevisedAt { get; init; }

    /// <summary>Gets when the draft was given up, or <see langword="null" /> while it stands.</summary>
    /// <remarks>
    /// A draft is marked here before its copies are taken out of the folder, and its row is removed once they are. The
    /// value therefore exists only inside a deletion that has not finished, which is exactly the window a resumed
    /// attempt has to recognize.
    /// </remarks>
    public required DateTimeOffset? DiscardedAt { get; init; }

    /// <summary>Gets the outgoing record a promotion wrote, or <see langword="null" /> while the draft was never promoted.</summary>
    /// <remarks>
    /// A promoted draft is not deleted at once. The message is queued rather than sent, so the draft stands until the
    /// send is delivered — a promotion whose delivery never succeeds must leave the owner their draft.
    /// </remarks>
    public required OutgoingEmailId? PromotedTo { get; init; }

    /// <summary>Gets every copy of this draft MailFathom has appended, newest revision first.</summary>
    public required IReadOnlyList<MailDraftServerCopy> Copies { get; init; }

    /// <summary>Gets why the tracked copy stopped being one MailFathom may touch, or <see langword="null" /> while none has.</summary>
    public required MailDraftDivergence? Divergence { get; init; }

    /// <summary>Gets the failure the last attempt on the mailbox ended in, or <see langword="null" /> while none has failed.</summary>
    /// <remarks>The code is kept and the message is not, for the reason an outgoing record keeps only the code.</remarks>
    public required MailFathomErrorCode? LastFailure { get; init; }

    /// <summary>Gets the copy carrying the revision the stored message is.</summary>
    public MailDraftServerCopy? CurrentCopy =>
        this.Copies.FirstOrDefault(copy => copy.Revision == this.Revision);

    /// <summary>Gets every copy a revision replaced that the folder still holds.</summary>
    /// <remarks>
    /// Ordinarily none or one. More than one is a deployment whose removals kept failing while its owner kept editing,
    /// which is why the copies are a list rather than a slot: each is a message in somebody's folder that only this
    /// record can still name.
    /// </remarks>
    public IReadOnlyList<MailDraftServerCopy> SupersededCopies =>
    [
        .. this.Copies.Where(copy => copy.Revision != this.Revision && copy.IsStanding),
    ];

    /// <summary>Gets whether the draft has been given up and is being taken out of the mailbox.</summary>
    public bool IsDiscarded => this.DiscardedAt is not null;

    /// <summary>Gets whether an append of this draft went out and the server's answer to it never came back.</summary>
    /// <remarks>
    /// It stops every later act on the mailbox, whichever revision the unanswered append was of. Appending again would
    /// put a second draft in the owner's folder beside a copy nobody can prove is there, and removing something means
    /// naming a UID the server never gave — so the draft goes on being edited here and its copy is left where it is.
    /// </remarks>
    public bool HasUnansweredAppend => this.Copies.Any(copy => copy.HasUnknownOutcome);

    /// <summary>Gets whether this draft names anybody to send it to.</summary>
    public bool IsAddressed => this.Recipients.Count > 0;

    /// <summary>Gets what the draft owes the mail server.</summary>
    public MailDraftStage Stage => this.ReadStage();

    /// <summary>Gets whether a promotion of this draft has yet to give the draft up.</summary>
    /// <remarks>
    /// Giving up is what delivery does to the draft it was promoted from, and it is written once — by the pass that
    /// delivered the send. So the mark is the only thing that says it happened, and a promoted draft still carrying
    /// none is one whose give-up never committed: a crash or a refused write between the delivery and the mark. It is
    /// outstanding until then, because otherwise its copy would stand in the owner's drafts folder for a message that
    /// has already gone out and nothing would ever reach it again.
    /// </remarks>
    public bool AwaitsPromotionGiveUp => this.PromotedTo is not null && !this.IsDiscarded;

    /// <summary>Gets whether an attempt against the mail server would do anything for this draft.</summary>
    /// <remarks>
    /// It is what a pass filters on, so an account whose drafts are all settled costs one bounded read rather than a
    /// session per draft. A draft whose append was never answered is deliberately not outstanding: nothing appends it
    /// again, and nothing can remove what nobody can name. A promoted draft is outstanding whatever its copies say,
    /// because what it is waiting for is its own delivery rather than a folder.
    /// </remarks>
    public bool HasOutstandingServerWork => this.AwaitsPromotionGiveUp
        || this.Stage
        is MailDraftStage.Composed
        or MailDraftStage.Discarded
        or MailDraftStage.ReplacementAppendPending
        or MailDraftStage.ReplacementRemovalPending;

    /// <summary>Finds the copy carrying one revision of this draft.</summary>
    /// <param name="revision">The revision to look for.</param>
    /// <returns>The copy, or <see langword="null" /> when that revision was never appended.</returns>
    public MailDraftServerCopy? FindCopy(int revision) =>
        this.Copies.FirstOrDefault(copy => copy.Revision == revision);

    /// <summary>Reads the stage from the copies, in the order that keeps each answer the strongest true one.</summary>
    /// <remarks>
    /// A discarded draft owes a removal whatever else is true of it, and an append nobody answered stops every later
    /// act on the mailbox — so both are read before the replacement pair, which is what is left once neither holds.
    /// </remarks>
    private MailDraftStage ReadStage()
    {
        if (this.IsDiscarded)
        {
            return MailDraftStage.Discarded;
        }

        if (this.HasUnansweredAppend)
        {
            return MailDraftStage.AppendIssued;
        }

        if (this.CurrentCopy is null)
        {
            return this.SupersededCopies.Count > 0
                ? MailDraftStage.ReplacementAppendPending
                : MailDraftStage.Composed;
        }

        return this.SupersededCopies.Count > 0
            ? MailDraftStage.ReplacementRemovalPending
            : MailDraftStage.Filed;
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery.Drafts;

/// <summary>States what one draft owes the mail server, read from the copies it has recorded.</summary>
/// <remarks>
/// <para>
/// It is derived rather than stored, because storing it would be a second statement of what the copies already say and
/// the two could disagree. What it names is the point a resumed attempt continues from: a process that dies partway
/// through a replacement is recognized by which of these the record reads as, and no other state is consulted.
/// </para>
/// <para>
/// The two replacement members are the reason the type exists. Editing a draft is an append followed by a removal, and
/// both orders of that pair lose something when a process dies between them — removing first can leave the owner with
/// no draft at all, and appending first can leave them with two. The record is written before the first command, so a
/// crash lands on one of these two members and the resumed attempt finishes the pair rather than starting it again.
/// </para>
/// </remarks>
public enum MailDraftStage
{
    /// <summary>The draft is stored here and no copy of it has been appended to the mailbox.</summary>
    Composed = 0,

    /// <summary>An append of the current revision went out and the server's answer to it never came back.</summary>
    /// <remarks>
    /// Nothing appends again on the strength of this, so the mailbox may or may not show the draft and no later
    /// revision replaces the copy. The divergence on the record is what an operator reads.
    /// </remarks>
    AppendIssued = 1,

    /// <summary>The current revision stands in the drafts folder and nothing it replaced is left there.</summary>
    Filed = 2,

    /// <summary>The current revision is not appended yet and the copy it replaces is still in the folder.</summary>
    /// <remarks>
    /// The owner sees the previous version of the draft, which is one draft rather than none. Resuming appends the
    /// current revision and then takes the previous one out.
    /// </remarks>
    ReplacementAppendPending = 3,

    /// <summary>The current revision is in the folder and so is the copy it replaces.</summary>
    /// <remarks>
    /// This is the crash window a replacement has, and the only one where the folder holds two copies of one draft.
    /// Resuming removes the copy that was replaced, which leaves exactly one.
    /// </remarks>
    ReplacementRemovalPending = 4,

    /// <summary>The draft has been given up and what is left is taking its copies back out of the folder.</summary>
    /// <remarks>
    /// The record outlives the decision to delete it on purpose: removing the row first would lose the only thing
    /// naming the copies, and the copies would stand in the owner's folder with nothing left able to remove them.
    /// </remarks>
    Discarded = 5,
}

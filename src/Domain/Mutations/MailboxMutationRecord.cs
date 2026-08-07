// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Domain.Mutations;

/// <summary>Reports what one durable mutation record holds: the change that was asked for and how far it has got.</summary>
/// <remarks>
/// <para>
/// The record is written before the first IMAP command and advanced as the sequence proceeds, which is what makes a
/// non-atomic sequence idempotent: a retry reads it and continues from the stage it names rather than starting over.
/// It is also what lets a change MailFathom made be told apart from the same change made by hand, which is a separate
/// reader of the same record rather than a second mechanism.
/// </para>
/// <para>
/// It is derived personal data. A mutation history says where a person's mail has been and what was done to it, so it
/// inherits the retention and deletion obligations of the email it describes and is removed with it.
/// </para>
/// </remarks>
public sealed record MailboxMutationRecord
{
    /// <summary>Gets what everything after the first write refers to this record by.</summary>
    public required MailboxMutationRecordId Id { get; init; }

    /// <summary>Gets the change that was asked for, restored exactly as it was written down.</summary>
    public required MailboxMutationRequest Request { get; init; }

    /// <summary>Gets how far along its protocol sequence the mutation has durably reached.</summary>
    public required MailboxMutationStage Stage { get; init; }

    /// <summary>Gets where the destination folder put the email, as far as the server has said.</summary>
    /// <remarks>
    /// It is <see cref="RemoteEmailPlacement.NotReported" /> both before the placement is confirmed and after a server
    /// that supplied no <c>COPYUID</c> response confirmed it. The two are told apart by <see cref="Stage" />, which is
    /// the value that says whether the placement has happened at all.
    /// </remarks>
    public required RemoteEmailPlacement Placement { get; init; }

    /// <summary>Gets whether the placement left a source occurrence that still has to be removed separately.</summary>
    /// <remarks>
    /// <para>
    /// Written when the placement command is issued and read by a resumed attempt, because it is the one thing about a
    /// half-finished relocation that cannot be worked out later. A relocation carried by <c>MOVE</c> removes the source
    /// as part of the same command and a relocation carried by copy does not, so a record stopped at
    /// <see cref="MailboxMutationStage.PlacementConfirmed" /> means opposite things depending on which ran.
    /// </para>
    /// <para>
    /// Re-deriving it from what the connection advertises at resume time is exactly what this replaces. A recovered
    /// connection can land on a server advertising something else, and a fallback relocation resumed against one
    /// reporting <c>MOVE</c> would be read as already complete — leaving the email in both folders permanently, with
    /// nothing left to surface it. That is the duplication this record exists to prevent, so the answer is durable
    /// rather than inferred.
    /// </para>
    /// <para>
    /// It is a fact about the sequence rather than a name for the operation. Which protocol path carried a relocation
    /// still reaches no log above <c>Debug</c>, no span, and no metric dimension, exactly as
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>
    /// requires.
    /// </para>
    /// </remarks>
    public required bool RequiresSourceRemoval { get; init; }

    /// <summary>Gets how many times this mutation has been attempted, counted before each attempt rather than after it.</summary>
    /// <remarks>
    /// Counting first is what makes the bound survive a crash loop: an attempt that kills the process still counted, so a
    /// mutation that crashes the host every time reaches its terminal stage instead of being retried forever.
    /// </remarks>
    public required int AttemptCount { get; init; }

    /// <summary>Gets when the intent was first written down.</summary>
    public required DateTimeOffset RecordedAt { get; init; }

    /// <summary>Gets when the record last moved, which is what says how long a stuck mutation has been stuck.</summary>
    public required DateTimeOffset StageChangedAt { get; init; }

    /// <summary>Gets the failure the last attempt ended in, or <see langword="null" /> while no attempt has failed.</summary>
    /// <remarks>
    /// The code is kept and the message is not. A code is a stable identity an operator can look up, while a message is
    /// text assembled at the failure site, and the record is read by an operator asking which mutations are stuck rather
    /// than by anybody re-reading a log line.
    /// </remarks>
    public required MailFathomErrorCode? LastFailure { get; init; }

    /// <summary>Gets whether the record has reached a stage nothing moves it out of.</summary>
    public bool IsTerminal => this.Stage is MailboxMutationStage.Completed or MailboxMutationStage.Abandoned;
}

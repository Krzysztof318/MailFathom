// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Domain.Delivery.Drafts;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Publishes the draft this deployment now holds, which has been offered to nobody.</summary>
/// <remarks>
/// <para>
/// It answers the two tools that write a draft, because what each of them leaves is the same thing: one draft, at one
/// version, that the owner's folder either shows yet or does not. A caller that saved and then updated reads the same
/// properties and sees the version move, which is what tells it the edit replaced the message rather than adding a
/// second one.
/// </para>
/// <para>
/// Nothing about the message appears. No address, no subject, no body, and no <c>Message-ID</c> — the identity and the
/// account are MailFathom's own names for things, and the recipient count says how many people the draft is addressed
/// to without saying who any of them are. A caller that wants any of the rest already holds what it sent.
/// </para>
/// </remarks>
[Description("The draft this deployment now holds. Nothing has been sent and nobody has been offered anything: a draft leaves only when send_draft is called for it.")]
internal sealed record SaveDraftToolResult
{
    /// <summary>Gets the stable identity of the draft.</summary>
    [Description("The stable identifier of the draft. It is what update_draft, delete_draft, and send_draft name it by, and it does not change when the draft is edited.")]
    public required string DraftId { get; init; }

    /// <summary>Gets the account the draft belongs to.</summary>
    [Description("The configured MailFathom account identifier the draft belongs to, and the one it would be sent as. Its Delivery configuration decides the From address, which a caller never supplies.")]
    public required string AccountId { get; init; }

    /// <summary>Gets whether the owner's drafts folder shows this version of the draft.</summary>
    [Description("Whether the owner's own drafts folder shows this version of the draft yet. The draft is held here either way and can be sent either way.")]
    public required SavedDraftState State { get; init; }

    /// <summary>Gets which version of the draft the stored message is.</summary>
    [Description("Which version of the draft this is, counted from one. Every accepted update_draft call adds one, and the folder ends up showing one message rather than a version apiece.")]
    public required int Revision { get; init; }

    /// <summary>Gets how many people the draft is addressed to.</summary>
    [Description("How many people the draft is addressed to across its to, cc, and bcc headers, after addresses named twice were reduced to one. Nobody is named. A draft addressed to nobody is an ordinary draft that send_draft refuses until it is addressed.")]
    public required int RecipientCount { get; init; }

    /// <summary>Gets when this version of the draft was written down.</summary>
    [Description("When this version of the draft was written down, as an ISO 8601 timestamp.")]
    public required DateTimeOffset SavedAt { get; init; }

    /// <summary>Publishes the draft the book wrote down.</summary>
    /// <param name="draft">The durable record.</param>
    /// <returns>The wire representation of <paramref name="draft" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="draft" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the record carries a stage no draft a caller just wrote can be in, which is a stage added without deciding what a caller should be told about it.</exception>
    public static SaveDraftToolResult From(MailDraftRecord draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return new SaveDraftToolResult
        {
            DraftId = draft.Id.ToString(),
            AccountId = draft.AccountId.Value,
            State = Published(draft.Stage),
            Revision = draft.Revision,
            RecipientCount = draft.Recipients.Count,
            SavedAt = draft.RevisedAt,
        };
    }

    /// <summary>Reads the state a stage is published under.</summary>
    /// <remarks>
    /// Written out rather than compared against one member, so a stage added to the record has to be given a published
    /// spelling here before it can reach a client. Four stages collapse into one published value because the four
    /// differ in what the mailbox still owes rather than in anything a caller acts on: each of them says the copy is
    /// not this version yet, and the settling pass is what changes that. A discarded draft is not among them, because
    /// a draft the caller has just written is one the book refused to write over a deleted one.
    /// </remarks>
    private static SavedDraftState Published(MailDraftStage stage) => stage switch
    {
        MailDraftStage.Filed => SavedDraftState.Filed,
        MailDraftStage.Composed
            or MailDraftStage.AppendIssued
            or MailDraftStage.ReplacementAppendPending
            or MailDraftStage.ReplacementRemovalPending => SavedDraftState.Held,
        _ => throw new ArgumentOutOfRangeException(
            nameof(stage),
            stage,
            "The mail draft stage is not one a draft that was just written can be in."),
    };
}

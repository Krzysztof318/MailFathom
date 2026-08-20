// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.Mail.Delivery.Drafts;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Publishes what giving up one draft did, which is durable by the time it is read.</summary>
/// <remarks>
/// It names the draft and says what became of the copy in the mailbox, and carries nothing about the message that was
/// given up: no address, no subject, and no line of what somebody wrote. What a caller has afterwards is the identifier
/// it already held, which now names nothing.
/// </remarks>
[Description("What giving up the draft did. The draft is gone from this deployment; what the state says is whether the copy in the owner's own drafts folder went with it.")]
internal sealed record DeleteDraftToolResult
{
    /// <summary>Gets the stable identity of the draft that was given up.</summary>
    [Description("The identifier of the draft that was given up. It names no draft after this call, so a later call carrying it is refused as a draft this deployment does not hold.")]
    public required string DraftId { get; init; }

    /// <summary>Gets what became of the copies in the owner's mailbox.</summary>
    [Description("What became of the copy in the owner's drafts folder.")]
    public required DeletedDraftState State { get; init; }

    /// <summary>Publishes what settling the mailbox did for a given-up draft.</summary>
    /// <param name="result">What the attempt against the mailbox did.</param>
    /// <returns>The wire representation of <paramref name="result" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
    public static DeleteDraftToolResult From(MailDraftFilingResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new DeleteDraftToolResult
        {
            DraftId = result.DraftId.ToString(),
            State = Published(result),
        };
    }

    /// <summary>Reads the state one attempt is published under.</summary>
    /// <remarks>
    /// The outcome and the divergence are one answer here, because the removal reports both and a caller acts on
    /// neither separately: a discarded draft whose copy was left standing is a message the owner still sees, which is
    /// what <see cref="DeletedDraftState.CopyLeftBehind" /> says, and every other ending is the record marked and the
    /// folder still owing something. The remaining outcomes are the settling pass's own vocabulary rather than a
    /// deletion's, so they are published as the one thing they mean to whoever asked: it is not finished yet.
    /// </remarks>
    private static DeletedDraftState Published(MailDraftFilingResult result) => result switch
    {
        { Outcome: MailDraftFilingOutcome.Discarded, Divergence: not null } => DeletedDraftState.CopyLeftBehind,
        { Outcome: MailDraftFilingOutcome.Discarded } => DeletedDraftState.Deleted,
        _ => DeletedDraftState.Pending,
    };
}

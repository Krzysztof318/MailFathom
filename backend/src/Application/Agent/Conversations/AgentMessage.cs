// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Citations;

namespace MailFathom.Application.Agent.Conversations;

/// <summary>One turn of a conversation, read back out of the entries that wrote it.</summary>
/// <remarks>
/// <para>
/// One shape for both authors rather than two, because what a reader does with a turn is the same either way: it has
/// somebody who wrote it, a place in the order, and whatever that person or that run put in it. A question carries
/// text and the scope it was asked under and nothing else; an answer carries blocks, the sources they rest on, the
/// offers it made, the line the run last reported, and how it ended.
/// </para>
/// <para>
/// <strong>It is mail-derived throughout and is never logged.</strong> The text is somebody's own words, the blocks
/// quote their correspondence, the citations name messages in their mailbox, and the scope names what they asked about.
/// </para>
/// </remarks>
public sealed record AgentMessage
{
    /// <summary>Initializes one turn of a conversation.</summary>
    /// <param name="id">What addresses the message.</param>
    /// <param name="author">Who wrote it.</param>
    /// <param name="writtenAt">The place in the conversation it was written at.</param>
    /// <param name="text">What was written in words, and <see langword="null" /> for an answer that speaks in blocks alone.</param>
    /// <param name="scope">What the turn was written under, and <see langword="null" /> before anything in the conversation has stated one.</param>
    /// <param name="citations">The sources the blocks rest on, declared once each.</param>
    /// <param name="blocks">The blocks the answer was composed of, in the order they were composed.</param>
    /// <param name="proposedActions">What the answer offered the person, in the order it offered them.</param>
    /// <param name="status">The line the run last reported, and <see langword="null" /> where it reported none.</param>
    /// <param name="outcome">How the answer ended, and <see langword="null" /> while it is still being composed or where the turn is not an answer.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="citations" />, <paramref name="blocks" />, or <paramref name="proposedActions" /> is <see langword="null" />.</exception>
    public AgentMessage(
        AgentMessageId id,
        AgentMessageAuthor author,
        long writtenAt,
        PresentationText? text,
        AgentMessageScope? scope,
        IReadOnlyList<PresentationCitation> citations,
        IReadOnlyList<PresentationBlock> blocks,
        IReadOnlyList<AgentProposedAction> proposedActions,
        PresentationText? status,
        AgentAnswerOutcome? outcome)
    {
        ArgumentNullException.ThrowIfNull(citations);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(proposedActions);

        this.Id = id;
        this.Author = author;
        this.WrittenAt = writtenAt;
        this.Text = text;
        this.Scope = scope;
        this.Citations = citations;
        this.Blocks = blocks;
        this.ProposedActions = proposedActions;
        this.Status = status;
        this.Outcome = outcome;
    }

    /// <summary>Gets what addresses the message.</summary>
    public AgentMessageId Id { get; }

    /// <summary>Gets who wrote it.</summary>
    public AgentMessageAuthor Author { get; }

    /// <summary>Gets the place in the conversation it was written at.</summary>
    public long WrittenAt { get; }

    /// <summary>Gets what was written in words, and <see langword="null" /> for an answer that speaks in blocks alone.</summary>
    public PresentationText? Text { get; }

    /// <summary>Gets what the turn was written under, and <see langword="null" /> before anything in the conversation has stated one.</summary>
    /// <remarks>
    /// A question states it and an answer is read under the one its question stated, which is what the scope an answer
    /// was composed under means: the record carries it once, on the turn that asked, and every turn after it carries
    /// what is in force until another question states something else.
    /// </remarks>
    public AgentMessageScope? Scope { get; }

    /// <summary>Gets the sources the blocks rest on, declared once each, and empty where the turn rests on none.</summary>
    public IReadOnlyList<PresentationCitation> Citations { get; }

    /// <summary>Gets the blocks the answer was composed of, in the order they were composed.</summary>
    public IReadOnlyList<PresentationBlock> Blocks { get; }

    /// <summary>Gets what the answer offered the person, in the order it offered them.</summary>
    /// <remarks>Each carries the place it was offered at, so a reader drawing the answer can put an offer back among the blocks it was composed between.</remarks>
    public IReadOnlyList<AgentProposedAction> ProposedActions { get; }

    /// <summary>Gets the line the run last reported, and <see langword="null" /> where it reported none.</summary>
    /// <remarks>The most recent one and nothing older, because that is what the line is: one changing statement of where the run has got to rather than a log of where it has been.</remarks>
    public PresentationText? Status { get; }

    /// <summary>Gets how the answer ended, and <see langword="null" /> while it is still being composed or where the turn is not an answer.</summary>
    public AgentAnswerOutcome? Outcome { get; }
}

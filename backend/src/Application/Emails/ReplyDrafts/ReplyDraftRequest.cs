// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.ReplyDrafts;

/// <summary>States which message a reply is being drafted to, and what its author asked for.</summary>
/// <remarks>
/// <para>
/// The message is named by its stable local identity and by nothing else, exactly as an answer somebody writes by hand
/// names it: the conversation the draft is grounded in, the account whose sent mail its style is derived from, and the
/// people it may propose are all read out of the stored copy that identity resolves to, so a caller can state none of
/// them and can state none of them wrongly.
/// </para>
/// <para>
/// The two texts are what makes this a drafting request rather than a reading. A selection is the part of the
/// correspondence somebody pointed at — the paragraph they are actually answering — and an instruction is what they
/// want said. Both are optional, and a request carrying neither is the ordinary one: reply to this conversation.
/// </para>
/// <para>
/// <b>A request naming no message is the composer with nothing behind it</b>, which the same drafting answers because
/// it is the same act: somebody asking for text they will read, edit, and decide about. What changes is that there is
/// no correspondence to ground it in, so nothing is read, nothing is cited, nobody is proposed, and the instruction is
/// the whole of the request — which is why it is required there and optional beside a message.
/// </para>
/// </remarks>
public sealed record ReplyDraftRequest
{
    /// <summary>The greatest length an instruction may have.</summary>
    /// <remarks>A sentence or two saying what to say. Somebody writing more than this is writing the reply, which is the work they asked to be spared.</remarks>
    public const int MaximumInstructionLength = 1_000;

    /// <summary>The greatest length a selection may have.</summary>
    /// <remarks>The bound one message of the conversation already travels under, because a selection is part of one and a longer one would be the exchange sent twice.</remarks>
    public const int MaximumSelectionLength = 4_000;

    /// <summary>Gets the stored message the reply answers, or <see langword="null" /> where the message being written answers none.</summary>
    public StoredEmailId? AnsweredEmailId { get; init; }

    /// <summary>Gets the part of the correspondence the draft is to answer, or <see langword="null" /> to answer the conversation as a whole.</summary>
    public string? Selection { get; init; }

    /// <summary>Gets what the person asked the reply to say, or <see langword="null" /> where they asked for nothing in particular.</summary>
    public string? Instruction { get; init; }
}

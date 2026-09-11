// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Chat;

/// <summary>Declares whether a reply is drafted from a conversation, and whether its manner comes from the account's own sent mail.</summary>
/// <remarks>
/// <para>
/// A block inside <c>Chat</c> rather than a root of its own, for the reason every derivation beside it is one: a
/// drafting runs against the declared chat endpoint and has nowhere to send a conversation without one.
/// </para>
/// <para>
/// <strong>On by default, on the same reading that puts the phrase search on beside it: what decides the default is
/// who spends the money and when.</strong> A drafting runs when a person presses the button that asks for one, which
/// is the same shape as asking a question and is offered by every deployment declaring an endpoint — unlike
/// enrichment, which runs over mail as it arrives and would put an operator's bill behind a mailbox somebody else
/// fills. Turning it off is therefore a decision about cost, and a deployment that takes it serves the composer
/// somebody writes in themselves, which is what every deployment served before this existed and what one without a
/// provider serves now.
/// </para>
/// <para>
/// The second switch is here rather than folded into the first because the two decisions are about different mail.
/// Drafting reads the conversation somebody is already reading; deriving a manner reads their own recent sent messages,
/// which is mail outside the exchange and the one part of this an operator may reasonably want off while keeping the
/// rest. A deployment that turns it off drafts from the correspondence alone, and the reply is plainer rather than
/// absent.
/// </para>
/// <para>
/// No numbers, for the reason the derivations beside it carry none: what one drafting costs is a provider call, and
/// what a deployment may spend on those in total is declared once, in <c>MailAnswering</c>. Every drafting is admitted
/// against those period ceilings and counted by the same ledgers, so it competes for one allowance with the questions
/// somebody asks.
/// </para>
/// </remarks>
internal sealed class ReplyDraftingOptions
{
    /// <summary>Gets or sets whether a reply is drafted from the conversation it answers.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets whether the draft's manner is derived from the answering account's own recent sent mail.</summary>
    /// <remarks>On where drafting is on, because a reply that does not sound like its sender is one somebody rewrites rather than edits, and the mail it reads is their own.</remarks>
    public bool StyleFromSentMail { get; set; } = true;
}

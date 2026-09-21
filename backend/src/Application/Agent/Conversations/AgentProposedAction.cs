// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;

namespace MailFathom.Application.Agent.Conversations;

/// <summary>Something the answer offered the person, and where that offer stands.</summary>
/// <param name="ProposedAt">The place in the conversation the offer was written at, which is what names it.</param>
/// <param name="Block">The block presenting the offer.</param>
/// <param name="State">Where it stands, which is pending until something is recorded against it.</param>
/// <remarks>
/// <para>
/// Read out of the record rather than stored as a thing of its own: the offer is the entry that was written when the
/// answer composed it, and the state is whatever the newest entry answering it says. That is what lets the same offer
/// be made twice in one conversation — declining one and proposing another time writes a second offer at a second
/// place, and the two carry their own outcomes because they carry their own places.
/// </para>
/// <para>
/// Nothing here says the action was carried out. An accepted proposal is one this record permits to be carried out;
/// what happened next reaches the record only as <see cref="AgentProposalState.Failed" />, or not at all.
/// </para>
/// </remarks>
public sealed record AgentProposedAction(long ProposedAt, PresentationBlock Block, AgentProposalState State);

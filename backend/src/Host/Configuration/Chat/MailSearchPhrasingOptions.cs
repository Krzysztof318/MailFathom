// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Chat;

/// <summary>Declares whether a sentence typed into the search field is read into the filters it describes.</summary>
/// <remarks>
/// <para>
/// A block inside <c>Chat</c> rather than a root of its own, because a reading runs against the declared chat endpoint
/// and has nowhere to send a sentence without one. An operator who removes the chat section has removed this with it,
/// which is the honest reading of what they did.
/// </para>
/// <para>
/// <strong>On by default, unlike enrichment beside it, and the difference is who spends the money.</strong> Enrichment
/// runs over mail as it arrives, so leaving it on would put an operator's provider bill behind a mailbox somebody else
/// fills. A reading runs when a person types a sentence and presses search, which is the same shape as asking a
/// question — and that is offered by every deployment declaring an endpoint. Turning it off is therefore a decision
/// about cost rather than about safety, and a deployment that takes it goes on searching by words exactly as it did.
/// </para>
/// <para>
/// It is one switch and no numbers, for the reason enrichment carries none: what one reading costs is a short provider
/// call, and what a deployment may spend on those in total is already declared once, in <c>MailAnswering</c>. Every
/// reading is admitted against the same period ceilings a question is and counted by the same ledgers, so searches and
/// questions compete for one allowance.
/// </para>
/// </remarks>
internal sealed class MailSearchPhrasingOptions
{
    /// <summary>Gets or sets whether a typed sentence is read into filters and criteria before the search runs.</summary>
    public bool Enabled { get; set; } = true;
}

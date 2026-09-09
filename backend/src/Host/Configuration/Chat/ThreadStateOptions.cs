// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Chat;

/// <summary>Declares whether a conversation is read into the state a client draws beside it.</summary>
/// <remarks>
/// <para>
/// A block inside <c>Chat</c> rather than a root of its own, for the reason enrichment beside it is one: a derivation
/// runs against the declared chat endpoint and has nowhere to send a conversation without one.
/// </para>
/// <para>
/// Off by default, and off is not a lesser deployment. A conversation then reads as it read before this existed, and a
/// client draws the absence of a state as a conversation nothing has been derived about rather than as a failure.
/// </para>
/// <para>
/// One switch and no numbers, for the reason enrichment carries none: what a deployment may spend on derivations in
/// total is declared once, in <c>MailAnswering</c>, and every derivation is admitted against those period ceilings and
/// counted by the same ledgers. So this competes for one allowance with enrichment and with the questions somebody
/// asks, and raising those ceilings is the operator's decision because it is their provider bill.
/// </para>
/// </remarks>
internal sealed class ThreadStateOptions
{
    /// <summary>Gets or sets whether a conversation is read into a state as its messages arrive.</summary>
    public bool Enabled { get; set; }
}

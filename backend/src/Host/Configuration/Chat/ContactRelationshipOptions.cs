// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Chat;

/// <summary>Declares whether an opened contact is read into a note about where the correspondence with them stands.</summary>
/// <remarks>
/// <para>
/// A block inside <c>Chat</c> rather than a root of its own, for the reason every derivation beside it is one: the
/// derivation runs against the declared chat endpoint and has nowhere to send a correspondence without one.
/// </para>
/// <para>
/// <strong>On by default, on the same reading that puts the drafting and the phrase search on beside it: what decides
/// the default is who spends the money and when.</strong> A card is derived when somebody opens one contact, which is
/// the shape of asking a question rather than of enrichment running over mail as it arrives — nothing here sweeps the
/// book, and a contact nobody opened costs nothing. Turning it off is therefore a decision about cost, and a
/// deployment that takes it draws the contact page it always drew: the record, the conversations naming that person,
/// and the documents they sent.
/// </para>
/// <para>
/// One switch rather than two, unlike the drafting beside it. What a derivation reads is settled by the correlation
/// the contact page already performs — there is no second body of mail to decline, and no bound here that an operator
/// could move without moving what an opened contact shows.
/// </para>
/// <para>
/// No numbers, for the reason the derivations beside it carry none: what one derivation costs is a provider call, and
/// what a deployment may spend on those in total is declared once, in <c>MailAnswering</c>. Every derivation is
/// admitted against those period ceilings and counted by the same ledgers, so it competes for one allowance with the
/// questions somebody asks — and a period already spent withholds the card rather than failing the contact.
/// </para>
/// </remarks>
internal sealed class ContactRelationshipOptions
{
    /// <summary>Gets or sets whether an opened contact is read into a relationship note and a suggested next action.</summary>
    public bool Enabled { get; set; } = true;
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Detection;

namespace MailFathom.Application.Contacts.Relationship;

/// <summary>Reads one correlated correspondence into where the relationship with that person stands.</summary>
/// <remarks>
/// <para>
/// The port an opened contact reaches the model through, and the whole of what a provider decides about a card. It
/// answers with statements and the citations under them, so nothing provider-shaped travels beyond this boundary.
/// </para>
/// <para>
/// It is registered only where the deployment declared a chat endpoint and turned the card on, so a caller resolves it
/// optionally and a deployment without one serves the contact page it always served — the record, the conversations,
/// and the documents — rather than a card that fails. That is a registration rather than a branch inside a call: there
/// is no path by which a correspondence leaves an instance whose operator did not ask for this.
/// </para>
/// </remarks>
public interface IContactRelationshipDeriver
{
    /// <summary>Derives one card, or answers with nothing where none could be derived.</summary>
    /// <param name="brief">The correlated correspondence and the language the card is written in.</param>
    /// <param name="cancellationToken">Cancels the derivation.</param>
    /// <returns>The card, or <see cref="ContactRelationship.Nothing" /> where the allowance was spent, the provider failed, or the answer was unreadable.</returns>
    /// <remarks>
    /// <b>Nothing a derivation runs into travels as a failure</b>, which is where this parts company with a drafted
    /// reply. A draft is what somebody pressed a button for, so a spent allowance has to be said out loud; a card
    /// arrives because a page was opened, and refusing the page over it would turn an operator's ceiling into a
    /// contact nobody can read. What a reader has for every one of those cases is the contact page without the card.
    /// </remarks>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the correspondence carries, which withholds the derivation rather than sending text nothing scanned.</exception>
    Task<ContactRelationship> DeriveAsync(ContactRelationshipBrief brief, CancellationToken cancellationToken);
}

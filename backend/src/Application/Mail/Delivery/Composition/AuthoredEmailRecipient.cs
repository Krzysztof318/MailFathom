// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;
using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Composition;

/// <summary>Names one person a message is addressed to by an address, whoever decided which address that is.</summary>
/// <param name="Role">The header the author wants this person named in.</param>
/// <param name="Address">The addr-spec to compose with, unparsed and unvalidated.</param>
/// <param name="DisplayName">The name to write beside the address, or nothing to write the address alone.</param>
/// <param name="Contact">The contact the address was resolved from, or nothing when an author supplied the address itself.</param>
/// <param name="Provenance">How the address came to be here, which is what the sending governance judges a caller's own word by.</param>
/// <remarks>
/// <para>
/// Both text members are an author's input rather than a value this system produced, which is why they arrive as text.
/// Parsing, normalizing, and refusing them is the composer's, so the shape a caller hands over is what it was given —
/// a boundary that repaired an address before this point would compose a message to somebody nobody named.
/// </para>
/// <para>
/// A recipient addressed by naming somebody in the contact book arrives here as an address like any other, which is what
/// keeps the book out of the composition entirely. What the contact adds is the identity beside it, which the outgoing
/// record keeps so that what was sent stays answerable after the book changes; nothing composes from it, and no path
/// resolves one here — <see cref="Addressing.NamedRecipientResolver" /> is the only place a contact becomes an address.
/// </para>
/// <para>
/// The display name reaches the composed message and nothing else. The outgoing record holds addresses because a send
/// cannot be resumed without them; a name is presentation, so it stays in the stored MIME the way every other authored
/// field does.
/// </para>
/// <para>
/// The provenance reaches neither. It says where the address came from rather than what it is, which is a question only
/// the governance in front of the outbox asks, and it defaults to the caller's own word so that a boundary added later
/// is judged strictly rather than trusted by omission.
/// </para>
/// </remarks>
public sealed record AuthoredEmailRecipient(
    OutgoingRecipientRole Role,
    string Address,
    string? DisplayName = null,
    ContactId? Contact = null,
    AuthoredRecipientProvenance Provenance = AuthoredRecipientProvenance.NamedByCaller);

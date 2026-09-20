// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts.Correspondence;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Contacts.Relationship;

/// <summary>Everything one relationship derivation is scoped to.</summary>
/// <param name="Correspondence">The conversations naming this person and the documents they sent, as the correlation just read them.</param>
/// <param name="Language">The language the card is written in, which is the language the person who opened the contact reads.</param>
/// <remarks>
/// <para>
/// The correlation and nothing else. A derivation is about one person the caller opened, so what it may read is what
/// reading that person already produced — no other contact, no message body, no mailbox beyond the correlation's own
/// bounded window, and nothing about anybody the caller did not open.
/// </para>
/// <para>
/// The person is not named in it and neither are their addresses. A note reads the same written about <em>them</em> as
/// about a name, and the name is the one value in an opened contact that identifies somebody outside the exchange, so
/// it stays in this deployment.
/// </para>
/// </remarks>
public sealed record ContactRelationshipBrief(
    ContactCorrespondence Correspondence,
    MailUserLanguage Language);

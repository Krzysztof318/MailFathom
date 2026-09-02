// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery;

/// <summary>States how one recipient came to be on a message: whoever asked wrote the address, or this system derived it.</summary>
/// <remarks>
/// <para>
/// It exists because those are different claims about where an address came from, and only one of them can carry an
/// instruction a stranger wrote. Text a caller typed into a tool argument may have been read out of a message body
/// moments earlier and this system cannot see the difference; an address this system took out of the contact book or
/// out of the headers of the very message being answered was never the caller's to choose.
/// </para>
/// <para>
/// It is read by the governance that decides whether a caller may address somebody nothing here vouches for, and it is
/// on the authored recipient rather than on the outgoing one because a record of a send has already met that
/// governance: what such a record has to hold is who the message went to, and how the address was arrived at stops
/// mattering once the send has been admitted.
/// </para>
/// <para>
/// A draft is the one thing written down before that governance ever runs, so a draft's recipients keep it. The
/// promotion is where the same question is finally asked, months later and against a contact book that has moved, and a
/// draft that had dropped the provenance would leave the promotion judging every address as the caller's own word.
/// </para>
/// </remarks>
public enum AuthoredRecipientProvenance
{
    /// <summary>Whoever asked for the message supplied this address as text.</summary>
    /// <remarks>The default, and the strict one: a recipient built without saying where it came from is treated as the caller's own word.</remarks>
    NamedByCaller = 0,

    /// <summary>The address is the one the owner's contact book holds for somebody the author named.</summary>
    ResolvedFromContactBook = 1,

    /// <summary>The address is one the answered message's own headers named, which a reply is addressed by.</summary>
    /// <remarks>
    /// It is a header of mail this deployment already holds rather than a body, which is what separates it from an
    /// address a caller read somewhere in a message: a sender that writes <c>Reply-To</c> is redirecting answers to
    /// itself, and the deployment's recipient policy is what bounds where those may go.
    /// </remarks>
    DerivedFromAnsweredEmail = 2,
}

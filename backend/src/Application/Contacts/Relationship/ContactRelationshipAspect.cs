// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Contacts.Relationship;

/// <summary>Names one thing a relationship derivation may observe about a correspondence.</summary>
/// <remarks>
/// <para>
/// The set is this deployment's rather than the model's. An answer carries a value under each member or leaves the
/// member out, so what a card draws beside a note is a label MailFathom chose and a value a producer wrote — a model
/// that invented its own labels would be composing the screen rather than reading the mail.
/// </para>
/// <para>
/// Three members rather than the five a contact card has room for, because a derivation observes what it was given.
/// The correspondence it is scoped to carries each exchange's subject and instant and each document's name and type,
/// which is enough to say when somebody is reachable, what is outstanding, and which matters the exchange is about —
/// and is not enough to say how quickly they answer or how they write, both of which need the messages themselves. A
/// member for either would be a label drawn over an answer nothing could ground.
/// </para>
/// </remarks>
public enum ContactRelationshipAspect
{
    /// <summary>When the correspondence with this person actually happens, as their messages fall across the day or the week.</summary>
    ActivePeriod = 0,

    /// <summary>What the correspondence leaves outstanding between the two of them, on either side.</summary>
    OpenItem = 1,

    /// <summary>Which matters the exchanges are about, read across the conversations rather than out of one of them.</summary>
    Case = 2,
}

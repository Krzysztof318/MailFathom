// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.BrowseThread;

/// <summary>Somebody who has written in a conversation, and how much of it is theirs.</summary>
/// <param name="Address">The address they wrote from, as the messages wrote it.</param>
/// <param name="DisplayName">The name their most recent message in the conversation wrote, or <see langword="null" /> where none of them carried one.</param>
/// <param name="MessageCount">How many of the conversation's assembled messages they sent.</param>
/// <remarks>
/// <para>
/// A participant is an author rather than an addressee. It is what a thread header draws — who is in this exchange and
/// how much each of them said — and it is derived from the whole conversation rather than from the page in hand, which
/// is the point of publishing it at all: a client that had to walk every message to name the participants would be
/// paging a conversation to draw its header.
/// </para>
/// <para>
/// The display name is the most recent one because it is the name that person currently writes under, and a header
/// showing the name they used two years ago would disagree with every other surface. It carries personal data —
/// an address and a name — and inherits the classification of the mail it was read from.
/// </para>
/// </remarks>
public sealed record ThreadParticipant(string Address, string? DisplayName, int MessageCount);

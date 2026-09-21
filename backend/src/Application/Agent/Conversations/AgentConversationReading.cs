// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Agent.Conversations;

/// <summary>One read of a conversation: what it is called, whether an answer is being composed, and everything written past the cursor the reader held.</summary>
/// <param name="Title">What the conversation is called, and <see langword="null" /> while nothing has named it.</param>
/// <param name="StartedAt">When the conversation was started, in UTC.</param>
/// <param name="Composing">Whether an answer is being composed, which is what tells a reader watching one that more is coming.</param>
/// <param name="Entries">Everything after the stated cursor, in order, and empty where the reader was already caught up.</param>
/// <param name="MoreFollows">Whether the conversation holds more past the last entry returned, which a reader collects by advancing its cursor and reading again.</param>
/// <remarks>
/// <para>
/// All of it together is what makes one route serve the first read and every later one. A reader holding nothing reads
/// from the beginning and is given the conversation so far; one holding a cursor is given the tail; and either is told
/// in the same answer whether an answer is still being composed and whether there is more to collect.
/// </para>
/// <para>
/// The title and the instant ride along because they are the conversation's own rather than any entry's, and a reader
/// that had to ask for them separately would be making two calls to draw one screen.
/// </para>
/// <para>
/// A conversation that does not exist or belongs to somebody else is not a value of this type — the read reports no
/// such conversation instead, which is one answer rather than a further state to read this apart from.
/// </para>
/// </remarks>
public sealed record AgentConversationReading(
    string? Title,
    DateTimeOffset StartedAt,
    bool Composing,
    IReadOnlyList<AgentConversationEntry> Entries,
    bool MoreFollows);

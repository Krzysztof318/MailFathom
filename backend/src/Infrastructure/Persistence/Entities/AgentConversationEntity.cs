// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Agent.Conversations;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One conversation a person is holding with the deployment's agent, for as long as they keep it.</summary>
/// <remarks>
/// <para>
/// The row is what makes a conversation reachable from every replica: the one composing an answer writes here, and any
/// of them answers a read for it. It carries no part of what was said — the entries beside it do — so what a replica
/// needs to decide whether a conversation exists, whether an answer is being composed, and whether the person asking
/// may see it is one narrow row.
/// </para>
/// <para>
/// <strong>The title is the one thing here that is the person's own material.</strong> The agent composes it from
/// their first question, so it is classified with the rest of the record and reaches no log; everything else in the row
/// is a generated identifier, a counter, and two instants.
/// </para>
/// <para>
/// <see cref="Sequence" /> is the conversation's own counter rather than a number derived from the entries, and that is
/// what serializes two writers against each other. A conversation genuinely has two: a person may type while a run is
/// composing, and both writes advance this column in one statement, so the row's own lock decides the order instead of
/// two readers of a maximum both finding the same next number.
/// </para>
/// <para>
/// It carries no concurrency token, for the reason the Discover run's row carries none: every statement against it is
/// composed and conditional on the state it read — an answer opens only while none is being composed, a part of an
/// answer is written only while that answer is the one being composed, and a proposal moves only from where it stands
/// — so what would be a read followed by a write is one statement PostgreSQL settles instead.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class AgentConversationEntity
{
    /// <summary>The table these rows live in, named here because every statement against it is composed.</summary>
    internal const string TableName = "agent_conversations";

    /// <summary>The key column, named here for the same reason the table is.</summary>
    internal const string IdColumnName = "Id";

    /// <summary>The column naming whose conversation it is, named here for the same reason the table is.</summary>
    internal const string UserIdColumnName = "UserId";

    /// <summary>The column holding what the conversation is called, named here for the same reason the table is.</summary>
    internal const string TitleColumnName = "Title";

    /// <summary>The column holding when the conversation was started, named here for the same reason the table is.</summary>
    internal const string StartedAtColumnName = "StartedAt";

    /// <summary>The column holding when anything was last written into it, named here for the same reason the table is.</summary>
    internal const string LastActivityAtColumnName = "LastActivityAt";

    /// <summary>The column holding how far the conversation's order has reached, named here for the same reason the table is.</summary>
    internal const string SequenceColumnName = "Sequence";

    /// <summary>The column holding how many entries of the visible history the conversation holds, named here for the same reason the table is.</summary>
    internal const string VisibleEntryCountColumnName = "VisibleEntryCount";

    /// <summary>The column naming the answer currently being composed, named here for the same reason the table is.</summary>
    internal const string ComposingMessageIdColumnName = "ComposingMessageId";

    /// <summary>The longest name the column takes, which is the bound the contract already states.</summary>
    internal const int TitleLengthLimit = AgentConversationBounds.MaximumTitleLength;

    /// <summary>Gets or sets the identifier the conversation is addressed by, which a client is handed and presents back.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the person whose conversation it is, which is who may read, add to, and delete it.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets what the conversation is called, and <see langword="null" /> while nothing has named it.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets when the conversation was started, in UTC.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Gets or sets when anything was last written into the conversation, in UTC.</summary>
    /// <remarks>What the person's history is ordered by, so it moves with every entry and with nothing else — naming a conversation is a fact about it rather than a turn of it and leaves this where it was.</remarks>
    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>Gets or sets how far the conversation's order has reached, which is also how many entries it holds.</summary>
    /// <remarks>Advanced inside the statement that writes an entry, which is what makes the place a row is written at the database's decision and what holds the record to its ceiling.</remarks>
    public long Sequence { get; set; }

    /// <summary>Gets or sets how many of the conversation's entries belong to the visible history.</summary>
    /// <remarks>
    /// The second counter beside <see cref="Sequence" />, advanced by the same statement only when the entry it writes is
    /// one a person reads back. It is what holds a conversation to the ceiling a person meets, so the technical history
    /// a run writes underneath — its tool traffic, its charges, its summaries — never shortens what a person can keep
    /// working in.
    /// </remarks>
    public long VisibleEntryCount { get; set; }

    /// <summary>Gets or sets the answer a run is composing into, and <see langword="null" /> while none is being composed.</summary>
    /// <remarks>
    /// Both the admission for everything a run writes and the answer to whether more is coming. A run that was stopped
    /// is stopped by clearing this, so the next part it tries to write is refused wherever that run is executing — which
    /// is how a control pressed on one replica reaches a run on another without anything having to reach it.
    /// </remarks>
    public Guid? ComposingMessageId { get; set; }
}

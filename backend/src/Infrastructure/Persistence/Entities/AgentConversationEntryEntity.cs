// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One thing that happened in a conversation, in the place it holds in that conversation's order.</summary>
/// <remarks>
/// <para>
/// <strong>This is mail-derived and sensitive throughout.</strong> A question is somebody's own words, a composed block
/// quotes their correspondence, a declared source names the message it was drawn from, and a scope names what they
/// asked about — so the payload is classified exactly as stored mail is: it reaches no log, no span, no metric, and no
/// failure message, and it goes when the conversation does and when the person beneath it does, through the cascade.
/// </para>
/// <para>
/// The conversation and the place together are the key, which is the promise the record makes: a place starts at one
/// and never skips, and the key is what makes a second row under one number impossible rather than unlikely. The place
/// comes from the conversation's own counter, advanced inside the statement that writes the row, so nothing outside the
/// database decides what a conversation has reached.
/// </para>
/// <para>
/// The kind is stored beside the payload although the payload's own discriminator carries it, because it is the one
/// thing about an entry that is read without reading what the entry said — whether an offer was made is a kind rather
/// than a quotation — and a row that has to be parsed to be understood is one an operator cannot look at without
/// reading somebody's mail.
/// </para>
/// <para>
/// <see cref="AnsweredProposalAt" /> and <see cref="ProposalState" /> are set on the rows that answer an offer and on
/// no others, and they are columns for the same reason the kind is: where a proposal stands is the one question the
/// database itself has to settle, because an acceptance is what permits an act with a side effect and two people
/// pressing at once must produce one. A statement that had to read the payload to decide could not decide it.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class AgentConversationEntryEntity
{
    /// <summary>The table these rows live in, named here because every statement against it is composed.</summary>
    internal const string TableName = "agent_conversation_entries";

    /// <summary>The column naming the conversation this belongs to, named here for the same reason the table is.</summary>
    internal const string ConversationIdColumnName = "ConversationId";

    /// <summary>The column holding the place in the conversation, named here for the same reason the table is.</summary>
    internal const string SequenceColumnName = "Sequence";

    /// <summary>The column holding which kind of entry this is, named here for the same reason the table is.</summary>
    internal const string KindColumnName = "Kind";

    /// <summary>The column holding the entry itself, named here for the same reason the table is.</summary>
    internal const string PayloadColumnName = "Payload";

    /// <summary>The column naming the offer this entry answers, named here for the same reason the table is.</summary>
    internal const string AnsweredProposalAtColumnName = "AnsweredProposalAt";

    /// <summary>The column holding where the answered offer now stands, named here for the same reason the table is.</summary>
    internal const string ProposalStateColumnName = "ProposalState";

    /// <summary>The longest kind the column takes.</summary>
    /// <remarks>A bound on the row rather than a statement of the format: what is written is one of the entry contract's own published names, and the column is given room past the longest of them so the names stay the contract's to choose.</remarks>
    internal const int KindLengthLimit = 64;

    /// <summary>The longest state the column takes, which is the name of one member of a closed set.</summary>
    internal const int ProposalStateLengthLimit = 32;

    /// <summary>The column holding when the entry was written, named here for the same reason the table is.</summary>
    internal const string WrittenAtColumnName = "WrittenAt";

    /// <summary>Gets or sets the conversation this entry belongs to.</summary>
    public Guid ConversationId { get; set; }

    /// <summary>Gets or sets the place this holds in the conversation, counted from one.</summary>
    public long Sequence { get; set; }

    /// <summary>Gets or sets the entry contract's own published name for this kind of entry.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Gets or sets the entry itself, as the conversation's own serialization contract writes it.</summary>
    /// <remarks>Held as JSON so a rolling upgrade's older build reads a newer build's row rather than failing on a column it does not know about, and as <c>json</c> rather than <c>jsonb</c> for the reason the configuration gives.</remarks>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Gets or sets the place the offer this entry answers was written at, and <see langword="null" /> on every entry that answers none.</summary>
    public long? AnsweredProposalAt { get; set; }

    /// <summary>Gets or sets where the answered offer now stands, by the name of the state, and <see langword="null" /> on every entry that answers none.</summary>
    /// <remarks>The name rather than a number, so the stored value survives the members of the set being renumbered and an operator reading the row needs no lookup table.</remarks>
    public string? ProposalState { get; set; }

    /// <summary>Gets or sets when the entry was written, in UTC.</summary>
    public DateTimeOffset WrittenAt { get; set; }
}

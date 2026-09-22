// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Agent.Conversations;

/// <summary>One thing that happened in a conversation, named against it and placed in its order.</summary>
/// <remarks>
/// <para>
/// A conversation is written as it happens rather than composed and saved: a question is one entry, the answer opening
/// is another, and every block, source, status line, proposal, and ending after it is one more. The hierarchy is closed
/// by a private protected constructor, so the entries declared beside it are the whole of it and a reader that handles
/// those handles every conversation this build writes.
/// </para>
/// <para>
/// <strong>Every entry carries its place, and the place is the conversation's rather than the answer's.</strong> The
/// sequence starts at one, increases by one, and is assigned where the row is written rather than by whatever composed
/// the entry — so a client renders in arrival order without sorting, and one holding a cursor asks for everything after
/// the last sequence it read. That cursor is why an answer survives a lost connection, a second screen of the same
/// person, and the replica composing it going away:
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0035-delivering-a-running-ai-answer-from-a-persisted-run-by-cursor-signal-and-re-read.md">ADR 0035</see>
/// is that decision, and the recovery is a property of this record rather than of any connection.
/// </para>
/// <para>
/// <strong>Most of it is mail-derived and sensitive throughout.</strong> A question is somebody's own words, a block
/// quotes their correspondence, a source names the message it was drawn from, and a scope names what they asked about.
/// All of it belongs in these rows and in the response that reads them back, and nowhere else — never in a log line, a
/// span attribute, an exception message, or the signal that says a conversation advanced.
/// </para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "entry")]
[JsonDerivedType(typeof(AgentMessageWritten), AgentMessageWritten.Kind)]
[JsonDerivedType(typeof(AgentAnswerStarted), AgentAnswerStarted.Kind)]
[JsonDerivedType(typeof(AgentStatusReported), AgentStatusReported.Kind)]
[JsonDerivedType(typeof(AgentCitationDeclared), AgentCitationDeclared.Kind)]
[JsonDerivedType(typeof(AgentBlockComposed), AgentBlockComposed.Kind)]
[JsonDerivedType(typeof(AgentActionProposed), AgentActionProposed.Kind)]
[JsonDerivedType(typeof(AgentAnswerEnded), AgentAnswerEnded.Kind)]
[JsonDerivedType(typeof(AgentProposalResolved), AgentProposalResolved.Kind)]
[JsonDerivedType(typeof(AgentToolCalled), AgentToolCalled.Kind)]
[JsonDerivedType(typeof(AgentToolAnswered), AgentToolAnswered.Kind)]
[JsonDerivedType(typeof(AgentModelCharged), AgentModelCharged.Kind)]
[JsonDerivedType(typeof(AgentConversationCompacted), AgentConversationCompacted.Kind)]
public abstract record AgentConversationEntry
{
    private protected AgentConversationEntry()
    {
    }

    /// <summary>Gets the conversation this happened in.</summary>
    /// <remarks>
    /// Carried on the entry rather than left to the read it arrived over, so a client holding several conversations
    /// never has to infer which one an entry belongs to from where it read it. It is the row's rather than the
    /// payload's, for the reason <see cref="Sequence" /> is.
    /// </remarks>
    [JsonIgnore]
    public AgentConversationId ConversationId { get; init; }

    /// <summary>Gets the place this holds in the conversation, counted from one.</summary>
    /// <remarks>
    /// Zero is what an entry that has not been written yet carries, and no written entry ever has it. Both this and
    /// <see cref="ConversationId" /> stay out of the stored payload: the place is derived inside the statement that
    /// writes the row, so an entry is serialized before either is known, and the store stamps both from the row it
    /// read them off. A payload carrying its own copy could only ever disagree with the row.
    /// </remarks>
    [JsonIgnore]
    public long Sequence { get; init; }

    /// <summary>Gets the name this entry is written under, which is the value the type discriminator carries.</summary>
    /// <remarks>
    /// <para>
    /// Declared here so anything naming an entry — a row's own kind column, say — uses the same word the JSON
    /// discriminator does rather than a second mapping that can disagree with it.
    /// </para>
    /// <para>
    /// <strong>Every override carries <see cref="JsonIgnoreAttribute" /> again.</strong> The serializer reads the
    /// attribute off the member it is serializing rather than off the one that member overrides, so an override without
    /// it writes the word a second time into the document beside the discriminator — and the same holds for the three
    /// members below, each of which is how the record is written rather than anything a client is told.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public abstract string EntryName { get; }

    /// <summary>Gets which of the conversation's two readings this entry belongs to.</summary>
    /// <remarks>
    /// Abstract rather than defaulted, so a kind added to the hierarchy cannot be declared without saying whether a
    /// person is shown it. Overridden with <see cref="JsonIgnoreAttribute" /> repeated, for the reason
    /// <see cref="EntryName" /> states.
    /// </remarks>
    [JsonIgnore]
    public abstract AgentConversationHistory History { get; }

    /// <summary>Gets the answer this entry is part of, and <see langword="null" /> where it belongs to no answer being composed.</summary>
    /// <remarks>
    /// What the store admits the entry against: an entry naming an answer is written only while that answer is the one
    /// the conversation is composing, which is how a run that was stopped writes nothing further without anything
    /// having to reach the replica executing it. A question and a resolution name none, because a person may ask and
    /// may answer a proposal while an answer is being composed.
    /// </remarks>
    [JsonIgnore]
    public virtual AgentMessageId? ComposedInto => null;

    /// <summary>Gets whether this entry opens the answer a run composes, after which that answer is the conversation's.</summary>
    /// <remarks>Overridden with <see cref="JsonIgnoreAttribute" /> repeated, for the reason <see cref="EntryName" /> states.</remarks>
    [JsonIgnore]
    public virtual bool OpensTheAnswer => false;

    /// <summary>Gets whether this entry ends the answer a run was composing, after which nothing further is written into it.</summary>
    /// <remarks>Overridden with <see cref="JsonIgnoreAttribute" /> repeated, for the reason <see cref="EntryName" /> states.</remarks>
    [JsonIgnore]
    public virtual bool EndsTheAnswer => false;
}

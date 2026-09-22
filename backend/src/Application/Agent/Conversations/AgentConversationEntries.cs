// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Citations;

namespace MailFathom.Application.Agent.Conversations;

/// <summary>Somebody wrote a message: the person asking, or the agent saying something in its own words.</summary>
/// <param name="MessageId">The message being written, which the client generated so that a post retried over a dropped connection creates no second one.</param>
/// <param name="Author">Who wrote it.</param>
/// <param name="Text">What they wrote.</param>
/// <param name="Scope">What the question was asked about, and <see langword="null" /> for a message of the agent's own.</param>
/// <remarks>
/// <para>
/// One entry for both authors, because a message is the same record whoever wrote it: a line of text placed in the
/// conversation's order. What differs is that a person's message says what it was asked about and the agent's says
/// nothing of the kind — the agent answers under the scope it was given rather than choosing one.
/// </para>
/// <para>
/// <strong>The agent writes one of these for a run that was cut short</strong>, saying so and that what arrived stays.
/// It is a message rather than a mark on the answer it followed, because it is the agent speaking about the run rather
/// than part of what the run composed, and the person is expected to say where to pick it up.
/// </para>
/// <para>
/// An answer the agent composes is not written this way: it opens with <see cref="AgentAnswerStarted" /> and says what
/// it has to say in blocks. That is the settled shape — an answer is a presentation plan rather than prose — and it is
/// why this entry carries text and no blocks.
/// </para>
/// </remarks>
/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="Author" /> is not a declared member.</exception>
/// <exception cref="ArgumentException">Thrown when <paramref name="Text" /> is the unspecified struct default.</exception>
public sealed record AgentMessageWritten(
    AgentMessageId MessageId,
    AgentMessageAuthor Author,
    PresentationText Text,
    AgentMessageScope? Scope)
    : AgentConversationEntry
{
    /// <summary>The value the type discriminator carries on the wire.</summary>
    public const string Kind = "message";

    /// <summary>Gets who wrote it.</summary>
    public AgentMessageAuthor Author { get; } = Requirement.Authored(Author, nameof(Author));

    /// <summary>Gets what was written, which is always something.</summary>
    public PresentationText Text { get; } = Requirement.Spoken(Text, nameof(Text));

    /// <inheritdoc />
    [JsonIgnore]
    public override string EntryName => Kind;

    /// <inheritdoc />
    [JsonIgnore]
    public override AgentConversationHistory History => AgentConversationHistory.Visible;
}

/// <summary>The agent's answer opens, and from here until it ends everything the run composes is written into it.</summary>
/// <param name="MessageId">The message the answer is composed into.</param>
/// <remarks>
/// It carries no text, because at the moment a run starts there is nothing to say yet: the status line, the sources,
/// the blocks, and the proposals all arrive afterwards and each is its own entry. Opening the message first is what
/// gives them somewhere to be written, and what lets a client draw the answer's place in the conversation before a
/// single block of it exists.
/// </remarks>
public sealed record AgentAnswerStarted(AgentMessageId MessageId) : AgentConversationEntry
{
    /// <summary>The value the type discriminator carries on the wire.</summary>
    public const string Kind = "answerStarted";

    /// <inheritdoc />
    [JsonIgnore]
    public override string EntryName => Kind;

    /// <inheritdoc />
    [JsonIgnore]
    public override AgentConversationHistory History => AgentConversationHistory.Visible;

    /// <inheritdoc />
    [JsonIgnore]
    public override bool OpensTheAnswer => true;
}

/// <summary>Where the run has got to, in one line meant to be read while it works.</summary>
/// <param name="MessageId">The answer being composed.</param>
/// <param name="Status">The line, which replaces whatever the run said last.</param>
/// <remarks>
/// <para>
/// A recorded entry rather than state in the replica composing the answer, which is the whole reason it is here: a
/// second screen of the same person, a reconnect, and a reload all have to see where the run stands, and only a
/// recorded line gives them that. A reader shows the most recent one and nothing older.
/// </para>
/// <para>
/// It is the agent's own words and therefore the person's language, and it is mail-derived in practice — *reading the
/// attachments on the contract thread* names a thread — so it is sensitive exactly as the rest of the record is.
/// </para>
/// </remarks>
/// <exception cref="ArgumentException">Thrown when <paramref name="Status" /> is the unspecified struct default.</exception>
public sealed record AgentStatusReported(AgentMessageId MessageId, PresentationText Status) : AgentConversationEntry
{
    /// <summary>The value the type discriminator carries on the wire.</summary>
    public const string Kind = "status";

    /// <summary>Gets the line the run is reporting, which is always something.</summary>
    public PresentationText Status { get; } = Requirement.Spoken(Status, nameof(Status));

    /// <inheritdoc />
    [JsonIgnore]
    public override string EntryName => Kind;

    /// <inheritdoc />
    [JsonIgnore]
    public override AgentConversationHistory History => AgentConversationHistory.Visible;

    /// <inheritdoc />
    [JsonIgnore]
    public override AgentMessageId? ComposedInto => this.MessageId;
}

/// <summary>A source the answer's blocks rest on, declared before anything names it.</summary>
/// <param name="MessageId">The answer being composed.</param>
/// <param name="Citation">The source, under the name the blocks refer to it by.</param>
/// <remarks>
/// Declared as its own entry for the reason a whole presentation plan declares its citations once: two facts drawn from
/// one message are visibly the same source, and a reader can list what an answer rested on before it has drawn a block.
/// It is per answer rather than per conversation, because a name is only ever resolved against the answer that declared
/// it — two answers reusing one name mean two different sources, and merging them across a conversation would make a
/// block cite a message it never read.
/// </remarks>
/// <exception cref="ArgumentNullException">Thrown when <paramref name="Citation" /> is <see langword="null" />.</exception>
public sealed record AgentCitationDeclared(AgentMessageId MessageId, PresentationCitation Citation)
    : AgentConversationEntry
{
    /// <summary>The value the type discriminator carries on the wire.</summary>
    public const string Kind = "citation";

    /// <summary>Gets the source, under the name the answer's blocks refer to it by.</summary>
    public PresentationCitation Citation { get; } = Citation ?? throw new ArgumentNullException(nameof(Citation));

    /// <inheritdoc />
    [JsonIgnore]
    public override string EntryName => Kind;

    /// <inheritdoc />
    [JsonIgnore]
    public override AgentConversationHistory History => AgentConversationHistory.Visible;

    /// <inheritdoc />
    [JsonIgnore]
    public override AgentMessageId? ComposedInto => this.MessageId;
}

/// <summary>A block of the answer is ready, and every source it names has already been declared.</summary>
/// <param name="MessageId">The answer being composed.</param>
/// <param name="Block">The block, in the place it holds in the answer's reading order.</param>
/// <remarks>
/// Written where the block is composed rather than with the rest at the end, which is what lets a person watch an
/// answer assemble and what leaves the blocks of a run that was cut short standing instead of losing them. A block that
/// offers the person something to do is not written this way — that is <see cref="AgentActionProposed" />, which is a
/// separate entry precisely so that a proposal can be told from a reading without parsing what it says.
/// </remarks>
/// <exception cref="ArgumentNullException">Thrown when <paramref name="Block" /> is <see langword="null" />.</exception>
/// <exception cref="ArgumentException">Thrown when the block is one a person acts on, which is proposed rather than composed.</exception>
public sealed record AgentBlockComposed(AgentMessageId MessageId, PresentationBlock Block) : AgentConversationEntry
{
    /// <summary>The value the type discriminator carries on the wire.</summary>
    public const string Kind = "block";

    /// <summary>Gets the block, in the place it holds in the answer's reading order.</summary>
    public PresentationBlock Block { get; } = Requirement.Reading(Block, nameof(Block));

    /// <inheritdoc />
    [JsonIgnore]
    public override string EntryName => Kind;

    /// <inheritdoc />
    [JsonIgnore]
    public override AgentConversationHistory History => AgentConversationHistory.Visible;

    /// <inheritdoc />
    [JsonIgnore]
    public override AgentMessageId? ComposedInto => this.MessageId;
}

/// <summary>The answer offers the person something to do, which nothing does until they say so.</summary>
/// <param name="MessageId">The answer being composed.</param>
/// <param name="Block">The block presenting the offer, which is one the person acts on.</param>
/// <param name="Act">What accepting the offer carries out, stated exactly enough that nothing has to be asked again.</param>
/// <remarks>
/// <para>
/// <strong>A proposal is a block and is addressed by its place.</strong> The catalogue already says which types a
/// person acts on, so nothing here invents a second shape for an offer; what this entry adds is that the offer is
/// answerable, and the sequence it was written under is the name the answer to it is recorded against. That is also
/// what lets the same offer be made twice in one conversation — at two places, with two outcomes — which is exactly
/// what proposing another time is.
/// </para>
/// <para>
/// It records the offer and never the carrying out of one. What an accepted action then did belongs to whatever
/// performs it, and reaches this record only as the state the proposal ends in.
/// </para>
/// <para>
/// <strong>The act travels with the block rather than being read back out of it.</strong> A block says what a person
/// needs to see, and an act says what is done — which account a message leaves from, which message a reply answers.
/// Keeping both on the one entry is what makes accepting the offer perform exactly what was offered.
/// </para>
/// </remarks>
/// <exception cref="ArgumentNullException">Thrown when <paramref name="Block" /> or <paramref name="Act" /> is <see langword="null" />.</exception>
/// <exception cref="ArgumentException">Thrown when the block is one a person only reads, which is composed rather than proposed.</exception>
public sealed record AgentActionProposed(AgentMessageId MessageId, PresentationBlock Block, AgentProposedAct Act)
    : AgentConversationEntry
{
    /// <summary>The value the type discriminator carries on the wire.</summary>
    public const string Kind = "proposal";

    /// <summary>Gets the block presenting the offer.</summary>
    public PresentationBlock Block { get; } = Requirement.Actionable(Block, nameof(Block));

    /// <summary>Gets what accepting the offer carries out.</summary>
    public AgentProposedAct Act { get; } = Act ?? throw new ArgumentNullException(nameof(Act));

    /// <inheritdoc />
    [JsonIgnore]
    public override string EntryName => Kind;

    /// <inheritdoc />
    [JsonIgnore]
    public override AgentConversationHistory History => AgentConversationHistory.Visible;

    /// <inheritdoc />
    [JsonIgnore]
    public override AgentMessageId? ComposedInto => this.MessageId;
}

/// <summary>The answer stopped being composed, and this says how.</summary>
/// <param name="MessageId">The answer that has ended.</param>
/// <param name="Outcome">How it stopped.</param>
/// <remarks>
/// Recorded rather than inferred from nothing further arriving, because a conversation read later has no other way to
/// tell an answer that finished from one whose replica went away mid-run. Nothing it composed is removed or rolled back
/// by any of the three outcomes: what arrived stays, and the record says what happened to the rest.
/// </remarks>
/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="Outcome" /> is not a declared member.</exception>
public sealed record AgentAnswerEnded(AgentMessageId MessageId, AgentAnswerOutcome Outcome) : AgentConversationEntry
{
    /// <summary>The value the type discriminator carries on the wire.</summary>
    public const string Kind = "answerEnded";

    /// <summary>Gets how the answer stopped being composed.</summary>
    public AgentAnswerOutcome Outcome { get; } = Requirement.Declared(Outcome, nameof(Outcome));

    /// <inheritdoc />
    [JsonIgnore]
    public override string EntryName => Kind;

    /// <inheritdoc />
    [JsonIgnore]
    public override AgentConversationHistory History => AgentConversationHistory.Visible;

    /// <inheritdoc />
    [JsonIgnore]
    public override AgentMessageId? ComposedInto => this.MessageId;

    /// <inheritdoc />
    [JsonIgnore]
    public override bool EndsTheAnswer => true;
}

/// <summary>A proposal moved out of the state it was in, and this is where it stands now.</summary>
/// <param name="ProposedAt">The place the offer was written at, which is what names it.</param>
/// <param name="State">Where it stands now.</param>
/// <remarks>
/// <para>
/// It names no answer, because a person answers a proposal whenever they like — including while a later answer is being
/// composed, and long after the conversation went quiet. What it is written against is the proposal's own place in the
/// order, which is the one name that cannot come to mean a different offer.
/// </para>
/// <para>
/// <see cref="AgentProposalState.Pending" /> is refused: it is the state of a proposal nothing has been recorded
/// against, so writing it would be recording that nothing was recorded.
/// </para>
/// </remarks>
/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="ProposedAt" /> is not a written place, or when <paramref name="State" /> is not a declared member.</exception>
/// <exception cref="ArgumentException">Thrown when the state is <see cref="AgentProposalState.Pending" />.</exception>
public sealed record AgentProposalResolved(long ProposedAt, AgentProposalState State) : AgentConversationEntry
{
    /// <summary>The value the type discriminator carries on the wire.</summary>
    public const string Kind = "resolution";

    /// <summary>Gets the place the offer being answered was written at.</summary>
    public long ProposedAt { get; } = Requirement.Written(ProposedAt, nameof(ProposedAt));

    /// <summary>Gets where the proposal stands now, which is never pending.</summary>
    public AgentProposalState State { get; } = Requirement.Resolved(State, nameof(State));

    /// <inheritdoc />
    [JsonIgnore]
    public override string EntryName => Kind;

    /// <inheritdoc />
    [JsonIgnore]
    public override AgentConversationHistory History => AgentConversationHistory.Visible;
}

/// <summary>The model asked for a tool while composing an answer, and this is what it asked for.</summary>
/// <param name="MessageId">The answer being composed.</param>
/// <param name="CallId">What the model named the call, which the tool's answer is written against.</param>
/// <param name="ToolName">The tool it asked for.</param>
/// <param name="Arguments">The arguments it passed, as the JSON document the model sent.</param>
/// <remarks>
/// <para>
/// <strong>It belongs to the technical history alone.</strong> A person sees what a run did through its status line and
/// what it composed; the call itself was written to compose the model's next input, and it is recorded so that input can
/// be rebuilt from the record rather than inferred from it. Together with <see cref="AgentToolAnswered" /> it is every
/// step a run's tool loop took, in the order it took them.
/// </para>
/// <para>
/// The arguments are the model's own and routinely name a thread, a person, or a search a person asked for, so they are
/// sensitive exactly as the rest of the record is.
/// </para>
/// </remarks>
/// <exception cref="ArgumentException">Thrown when <paramref name="CallId" /> or <paramref name="ToolName" /> is empty.</exception>
/// <exception cref="ArgumentNullException">Thrown when <paramref name="Arguments" /> is <see langword="null" />.</exception>
public sealed record AgentToolCalled(AgentMessageId MessageId, string CallId, string ToolName, string Arguments)
    : AgentConversationEntry
{
    /// <summary>The value the type discriminator carries on the wire.</summary>
    public const string Kind = "toolCall";

    /// <summary>Gets what the model named the call.</summary>
    public string CallId { get; } = Requirement.Named(CallId, nameof(CallId));

    /// <summary>Gets the tool the model asked for.</summary>
    public string ToolName { get; } = Requirement.Named(ToolName, nameof(ToolName));

    /// <summary>Gets the arguments the model passed, as it sent them.</summary>
    public string Arguments { get; } = Arguments ?? throw new ArgumentNullException(nameof(Arguments));

    /// <inheritdoc />
    [JsonIgnore]
    public override string EntryName => Kind;

    /// <inheritdoc />
    [JsonIgnore]
    public override AgentConversationHistory History => AgentConversationHistory.Technical;

    /// <inheritdoc />
    [JsonIgnore]
    public override AgentMessageId? ComposedInto => this.MessageId;
}

/// <summary>A tool answered a call the model made, and this is what the model was handed back.</summary>
/// <param name="MessageId">The answer being composed.</param>
/// <param name="CallId">The call this answers, as the model named it.</param>
/// <param name="Result">What the tool returned, as the document the model was sent.</param>
/// <remarks>
/// Technical alone, for the reason <see cref="AgentToolCalled" /> is. A tool's answer quotes the person's mail, calendar,
/// and tasks as the model read them, so it is the most mail-derived entry the record holds — and it is written here,
/// with the conversation, precisely so that it goes wherever the conversation goes and nowhere else.
/// </remarks>
/// <exception cref="ArgumentException">Thrown when <paramref name="CallId" /> is empty.</exception>
/// <exception cref="ArgumentNullException">Thrown when <paramref name="Result" /> is <see langword="null" />.</exception>
public sealed record AgentToolAnswered(AgentMessageId MessageId, string CallId, string Result) : AgentConversationEntry
{
    /// <summary>The value the type discriminator carries on the wire.</summary>
    public const string Kind = "toolResult";

    /// <summary>Gets the call this answers.</summary>
    public string CallId { get; } = Requirement.Named(CallId, nameof(CallId));

    /// <summary>Gets what the tool returned.</summary>
    public string Result { get; } = Result ?? throw new ArgumentNullException(nameof(Result));

    /// <inheritdoc />
    [JsonIgnore]
    public override string EntryName => Kind;

    /// <inheritdoc />
    [JsonIgnore]
    public override AgentConversationHistory History => AgentConversationHistory.Technical;

    /// <inheritdoc />
    [JsonIgnore]
    public override AgentMessageId? ComposedInto => this.MessageId;
}

/// <summary>What the first call of a run sent and what the provider charged for it.</summary>
/// <param name="MessageId">The answer whose run made the call.</param>
/// <param name="SentCharacters">How many characters of text the call sent.</param>
/// <param name="InputTokens">How many input tokens the provider reported charging for them.</param>
/// <remarks>
/// <para>
/// This is what the budget a turn is measured against is corrected from. A conversation's size is estimated in
/// characters, because that is what can be counted before anything is sent, and converted into tokens at the rate the
/// last turn was actually charged — so the estimate follows whatever model the deployment is running without this
/// repository keeping a tokenizer for anybody's model.
/// </para>
/// <para>
/// Only the first call is recorded, because it is the one whose input is the conversation itself: every later call of
/// the same run adds the run's own tool traffic, which says nothing about how the next turn's history will be charged.
/// A provider that reports no usage leaves no entry, and the estimate keeps the rate it had.
/// </para>
/// </remarks>
/// <exception cref="ArgumentOutOfRangeException">Thrown when either count is not positive.</exception>
public sealed record AgentModelCharged(AgentMessageId MessageId, long SentCharacters, long InputTokens)
    : AgentConversationEntry
{
    /// <summary>The value the type discriminator carries on the wire.</summary>
    public const string Kind = "charge";

    /// <summary>Gets how many characters of text the call sent.</summary>
    public long SentCharacters { get; } = Requirement.Counted(SentCharacters, nameof(SentCharacters));

    /// <summary>Gets how many input tokens the provider charged for them.</summary>
    public long InputTokens { get; } = Requirement.Counted(InputTokens, nameof(InputTokens));

    /// <inheritdoc />
    [JsonIgnore]
    public override string EntryName => Kind;

    /// <inheritdoc />
    [JsonIgnore]
    public override AgentConversationHistory History => AgentConversationHistory.Technical;

    /// <inheritdoc />
    [JsonIgnore]
    public override AgentMessageId? ComposedInto => this.MessageId;
}

/// <summary>What came earlier in the conversation was summarised, and this is the summary sent in its place from now on.</summary>
/// <param name="MessageId">The answer whose turn the compaction was taken for.</param>
/// <param name="Through">The last place the summary stands in for: everything from the conversation's beginning up to and including it.</param>
/// <param name="Summary">The summary the compaction produced, which already folds in the summary before it.</param>
/// <param name="Carried">The places of the proposals still pending when the compaction ran, which a turn carries verbatim beside the summary rather than inside it.</param>
/// <remarks>
/// <para>
/// <strong>A compaction adds and never replaces.</strong> Nothing the summary covers is removed, rewritten, or
/// overwritten — not the turns, not the tool traffic under them, and not the summary before this one — so what was said
/// stays recoverable whatever a summariser made of it. The record of a long conversation reads as the whole conversation
/// plus every summary taken over it, in the order both happened, and the row this is written as states when it ran.
/// </para>
/// <para>
/// <strong>Which summary a turn was sent is read from the order.</strong> A turn is composed from the newest compaction
/// written before its answer opened, followed by every turn written after the place that compaction covers; the answer
/// named here is the one whose turn first used it. So a turn's model input can be rebuilt from the record alone, and two
/// turns no compaction separates send the same leading part byte for byte — which is what a provider's prompt cache is
/// keyed on.
/// </para>
/// <para>
/// A proposal still pending is live work a summary could not be accepted from, so it is carried beside the summary
/// rather than folded into it, and the places carried are recorded here so every turn this compaction serves carries the
/// same ones.
/// </para>
/// </remarks>
/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="Through" /> is not a written place, or when a carried place is not one the summary covers.</exception>
/// <exception cref="ArgumentException">Thrown when <paramref name="Summary" /> is empty.</exception>
/// <exception cref="ArgumentNullException">Thrown when <paramref name="Carried" /> is <see langword="null" />.</exception>
public sealed record AgentConversationCompacted(
    AgentMessageId MessageId,
    long Through,
    string Summary,
    IReadOnlyList<long> Carried)
    : AgentConversationEntry
{
    /// <summary>The value the type discriminator carries on the wire.</summary>
    public const string Kind = "compaction";

    /// <summary>Gets the last place the summary stands in for.</summary>
    public long Through { get; } = Requirement.Written(Through, nameof(Through));

    /// <summary>Gets the summary sent in place of everything up to <see cref="Through" />.</summary>
    public string Summary { get; } = Requirement.Named(Summary, nameof(Summary));

    /// <summary>Gets the places of the proposals carried verbatim beside the summary.</summary>
    public IReadOnlyList<long> Carried { get; } = Requirement.Covered(Carried, Through, nameof(Carried));

    /// <inheritdoc />
    [JsonIgnore]
    public override string EntryName => Kind;

    /// <inheritdoc />
    [JsonIgnore]
    public override AgentConversationHistory History => AgentConversationHistory.Technical;

    /// <inheritdoc />
    [JsonIgnore]
    public override AgentMessageId? ComposedInto => this.MessageId;
}

/// <summary>The checks the entries above share, kept here so each states its rule once.</summary>
file static class Requirement
{
    internal static PresentationBlock Reading(PresentationBlock block, string parameter)
    {
        ArgumentNullException.ThrowIfNull(block, parameter);

        if (block.Type.Actionable)
        {
            throw new ArgumentException(
                $"A '{block.Type.Identity}' block offers the person something to do, so it is proposed rather than composed.",
                parameter);
        }

        return block;
    }

    internal static PresentationBlock Actionable(PresentationBlock block, string parameter)
    {
        ArgumentNullException.ThrowIfNull(block, parameter);

        if (!block.Type.Actionable)
        {
            throw new ArgumentException(
                $"A '{block.Type.Identity}' block is read rather than acted on, so it is composed rather than proposed.",
                parameter);
        }

        return block;
    }

    internal static AgentMessageAuthor Authored(AgentMessageAuthor author, string parameter) =>
        Enum.IsDefined(author)
            ? author
            : throw new ArgumentOutOfRangeException(parameter, author, "A turn is written by a declared author.");

    internal static PresentationText Spoken(PresentationText text, string parameter) =>
        text.IsSpecified
            ? text
            : throw new ArgumentException(
                "A turn says something, so the unspecified struct default is not a thing anybody wrote.",
                parameter);

    internal static AgentAnswerOutcome Declared(AgentAnswerOutcome outcome, string parameter) =>
        Enum.IsDefined(outcome)
            ? outcome
            : throw new ArgumentOutOfRangeException(parameter, outcome, "An answer ends in a declared outcome.");

    internal static long Written(long proposedAt, string parameter) =>
        proposedAt > 0
            ? proposedAt
            : throw new ArgumentOutOfRangeException(
                parameter,
                proposedAt,
                "A proposal is answered at the place it was written, which is counted from one.");

    internal static string Named(string value, string parameter) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A technical entry names what it records.", parameter)
            : value;

    internal static long Counted(long count, string parameter) =>
        count > 0
            ? count
            : throw new ArgumentOutOfRangeException(parameter, count, "A charge is recorded for a call that sent something and was charged for it.");

    internal static IReadOnlyList<long> Covered(IReadOnlyList<long> carried, long through, string parameter)
    {
        ArgumentNullException.ThrowIfNull(carried, parameter);

        if (carried.Any(place => place <= 0 || place > through))
        {
            throw new ArgumentOutOfRangeException(
                parameter,
                "A carried proposal is one the summary covers, so its place lies between the beginning and the last place covered.");
        }

        return carried;
    }

    internal static AgentProposalState Resolved(AgentProposalState state, string parameter)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(parameter, state, "A proposal moves to a declared state.");
        }

        if (state is AgentProposalState.Pending)
        {
            throw new ArgumentException(
                "Pending is the state of a proposal nothing has been recorded against, so it is never recorded.",
                parameter);
        }

        return state;
    }
}

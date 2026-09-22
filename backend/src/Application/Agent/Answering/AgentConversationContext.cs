// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Presentation.Blocks;

namespace MailFathom.Application.Agent.Answering;

/// <summary>The conversation before one question, read the way a turn is composed from it.</summary>
/// <remarks>
/// <para>
/// <strong>A turn is composed from the newest summary plus everything written after it, verbatim.</strong> The newest
/// <see cref="AgentConversationCompacted" /> in the record says which part of the conversation its summary stands in
/// for and which proposals ride beside it; every turn written after that part is sent as it was said. Nothing earlier in
/// the record changes between two turns that no compaction separates, so the leading part of what they send is the same
/// byte for byte — which is what a provider's prompt cache is keyed on, and why the oldest turns are not simply dropped
/// one by one as the conversation grows.
/// </para>
/// <para>
/// <strong>Two things are never folded into a summary.</strong> A proposal still pending is live work a summary could
/// not be accepted from, so it is carried beside the summary; and the scope the conversation stands under — the context
/// it was opened from, which the person still sees as a chip — is stated on the question's own turn rather than left
/// for a summary to paraphrase.
/// </para>
/// <para>
/// Its size is estimated in characters and converted into tokens at the rate the last turn was actually charged, which
/// <see cref="AgentModelCharged" /> records. A conversation no turn was charged for yet uses
/// <see cref="DefaultTokensPerCharacter" />.
/// </para>
/// </remarks>
internal sealed class AgentConversationContext
{
    /// <summary>The rate a conversation's size is converted at before any turn of it was charged.</summary>
    /// <remarks>Four characters to a token is the usual reading of English prose, and it is replaced by what the provider actually charged as soon as one turn has been answered.</remarks>
    internal const double DefaultTokensPerCharacter = 0.25;

    /// <summary>The words a summary is introduced by, so the model reads it as its own notes rather than as something said.</summary>
    internal const string SummaryPreamble =
        "[A summary of this conversation before the turns that follow, written for you and never shown to the person:]\n";

    private readonly IReadOnlyList<PlacedTurn> turns;
    private readonly Dictionary<long, PlacedTurn> proposals;
    private readonly IReadOnlySet<long> resolved;
    private readonly AgentConversationCompacted? newest;

    private AgentConversationContext(
        IReadOnlyList<PlacedTurn> turns,
        IReadOnlySet<long> resolved,
        AgentConversationCompacted? newest,
        AgentMessageScope? scopeInForce,
        double tokensPerCharacter)
    {
        this.turns = turns;
        this.proposals = turns.Where(static turn => turn.IsProposal).ToDictionary(static turn => turn.Place);
        this.resolved = resolved;
        this.newest = newest;
        this.ScopeInForce = scopeInForce;
        this.TokensPerCharacter = tokensPerCharacter;
    }

    /// <summary>Gets the scope the conversation stands under, which is the last one a question stated.</summary>
    internal AgentMessageScope? ScopeInForce { get; }

    /// <summary>Gets the rate characters are converted into tokens at, which the last charged turn set.</summary>
    internal double TokensPerCharacter { get; }

    /// <summary>Reads the technical history before a question.</summary>
    /// <param name="entries">Every entry written before the question, in order.</param>
    /// <returns>What a turn is composed from.</returns>
    internal static AgentConversationContext Read(IReadOnlyList<AgentConversationEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var charged = entries.OfType<AgentModelCharged>().LastOrDefault();

        return new AgentConversationContext(
            [.. entries.Select(TurnOf).OfType<PlacedTurn>()],
            entries.OfType<AgentProposalResolved>().Select(static resolution => resolution.ProposedAt).ToHashSet(),
            entries.OfType<AgentConversationCompacted>().LastOrDefault(),
            entries.OfType<AgentMessageWritten>().LastOrDefault(static message => message.Scope is not null)?.Scope,
            charged is null ? DefaultTokensPerCharacter : (double)charged.InputTokens / charged.SentCharacters);
    }

    /// <summary>Composes the history from the newest summary in the record.</summary>
    /// <returns>The summary, the proposals carried beside it, and every turn written after the part it covers.</returns>
    internal IReadOnlyList<AgentHistoryTurn> Compose() => this.ComposeFrom(this.newest);

    /// <summary>Composes the history from a compaction this turn has just taken.</summary>
    /// <param name="compaction">The compaction, which is newer than anything in the record this was read from.</param>
    /// <returns>Its summary, the proposals it carries, and every turn written after the part it covers.</returns>
    internal IReadOnlyList<AgentHistoryTurn> ComposeFrom(AgentConversationCompacted? compaction)
    {
        var through = compaction?.Through ?? 0;

        return
        [
            .. compaction is null ? [] : new[] { SummaryTurn(compaction.Summary) },
            .. (compaction?.Carried ?? []).Where(this.proposals.ContainsKey).Select(place => this.proposals[place].Turn),
            .. this.turns.Where(turn => turn.Place > through).Select(static turn => turn.Turn),
        ];
    }

    /// <summary>Estimates what a history and the question after it would be charged, in tokens.</summary>
    /// <param name="history">The history the turn would send.</param>
    /// <param name="question">The question's own text.</param>
    /// <returns>The estimate, rounded up.</returns>
    internal long EstimateTokens(IEnumerable<AgentHistoryTurn> history, string question) =>
        this.TokensOf(history.Sum(static turn => (long)turn.Text.Length) + question.Length);

    /// <summary>Decides what a compaction taken now would summarise, keeping the newest turns verbatim.</summary>
    /// <param name="budgetTokens">What a turn may send, in tokens.</param>
    /// <returns>The plan, or <see langword="null" /> where nothing written since the newest summary could be summarised without folding in a pending proposal.</returns>
    /// <remarks>
    /// The newest turns that fit in half the budget stay verbatim and everything older since the newest summary is
    /// folded into the next one, so a compaction leaves room for the conversation to grow for a while before the next
    /// is needed — each compaction changing the leading part a turn sends, which is what it costs. A proposal carried by
    /// the newest summary that has since been answered is folded in now, being no longer live.
    /// </remarks>
    internal AgentCompactionPlan? PlanCompaction(int budgetTokens)
    {
        var through = this.newest?.Through ?? 0;
        var after = this.turns.Where(turn => turn.Place > through).ToArray();
        var kept = this.NewestWithin(after, budgetTokens / 2);
        var summarised = after[..^kept];

        if (summarised.Length is 0)
        {
            return null;
        }

        var previouslyCarried = this.newest?.Carried ?? [];
        long[] carried =
        [
            .. previouslyCarried
                .Concat(summarised.Where(static turn => turn.IsProposal).Select(static turn => turn.Place))
                .Where(place => !this.resolved.Contains(place) && this.proposals.ContainsKey(place))
                .Order(),
        ];
        AgentHistoryTurn[] folded =
        [
            .. previouslyCarried
                .Where(this.proposals.ContainsKey)
                .Select(place => this.proposals[place])
                .Concat(summarised)
                .Where(turn => !carried.Contains(turn.Place))
                .OrderBy(static turn => turn.Place)
                .Select(static turn => turn.Turn),
        ];

        return folded.Length is 0
            ? null
            : new AgentCompactionPlan(this.newest?.Summary, folded, summarised[^1].Place, carried);
    }

    /// <summary>Composes as much of the history as fits the budget, for a turn whose compaction could not be taken.</summary>
    /// <param name="budgetTokens">What a turn may send, in tokens.</param>
    /// <param name="question">The question's own text.</param>
    /// <returns>The newest summary and what it carries, followed by the newest turns after it that still fit.</returns>
    /// <remarks>
    /// The oldest turns fall off here rather than being summarised, which changes the leading part every turn sends
    /// until a compaction succeeds again. It is the answer to a summariser that failed, never the ordinary path.
    /// </remarks>
    internal IReadOnlyList<AgentHistoryTurn> ComposeWithin(int budgetTokens, string question)
    {
        var through = this.newest?.Through ?? 0;
        AgentHistoryTurn[] leading = [.. this.ComposeFrom(this.newest).Take(this.LeadingCount())];
        var remaining = budgetTokens - this.EstimateTokens(leading, question);
        var after = this.turns.Where(turn => turn.Place > through).ToArray();
        var kept = remaining <= 0 ? 0 : this.NewestWithin(after, remaining);

        return [.. leading, .. after[^kept..].Select(static turn => turn.Turn)];
    }

    private static AgentHistoryTurn SummaryTurn(string summary) =>
        new(AgentMessageAuthor.Agent, string.Concat(SummaryPreamble, summary));

    /// <summary>Reads one entry into the turn it says, if it says one.</summary>
    /// <remarks>
    /// A proposal is reduced to a line saying what was offered, so a model is never shown an act in a form it could
    /// mistake for one it may repeat without asking; everything else a run writes is how an answer was laid out, which
    /// the next question does not need.
    /// </remarks>
    private static PlacedTurn? TurnOf(AgentConversationEntry entry) => entry switch
    {
        AgentMessageWritten message => new PlacedTurn(message.Sequence, new AgentHistoryTurn(message.Author, message.Text.Value), IsProposal: false),
        AgentBlockComposed { Block: AnswerBlock answer } => new PlacedTurn(
            entry.Sequence,
            new AgentHistoryTurn(AgentMessageAuthor.Agent, answer.Text.Value),
            IsProposal: false),
        AgentActionProposed { Block: DraftBlock draft } => new PlacedTurn(
            entry.Sequence,
            new AgentHistoryTurn(
                AgentMessageAuthor.Agent,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"[Proposed a message \"{draft.Subject.Value}\" to {string.Join(", ", draft.Recipients.Select(static recipient => recipient.Address))}.]")),
            IsProposal: true),
        _ => null,
    };

    private int LeadingCount() =>
        this.newest is null ? 0 : 1 + this.newest.Carried.Count(this.proposals.ContainsKey);

    /// <summary>Counts how many of the newest turns fit a number of tokens, stopping at the first that does not.</summary>
    private int NewestWithin(IReadOnlyList<PlacedTurn> candidates, long tokens)
    {
        var kept = 0;
        long characters = 0;

        foreach (var turn in candidates.Reverse())
        {
            characters += turn.Turn.Text.Length;

            if (this.TokensOf(characters) > tokens)
            {
                break;
            }

            kept++;
        }

        return kept;
    }

    private long TokensOf(long characters) => (long)Math.Ceiling(characters * this.TokensPerCharacter);

    /// <summary>One turn the history can send, beside the place it was written at.</summary>
    private sealed record PlacedTurn(long Place, AgentHistoryTurn Turn, bool IsProposal);
}

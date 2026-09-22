// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Application.Emails.Search;

namespace MailFathom.Application.Agent.Search;

/// <summary>Places what was said in one turn of a conversation in the active vector space, once the turn has ended.</summary>
/// <remarks>
/// <para>
/// <strong>What is embedded is what somebody said.</strong> The person's question, any instruction they added while it
/// was being answered, and the agent's prose — its answer block — are placed. Every other block reaches the lexical
/// index alone: it renders an object that lives elsewhere and is searched there by its own search, so embedding it
/// would rank the conversation for the meaning of a calendar entry rather than for anything said in it.
/// </para>
/// <para>
/// <strong>It runs when the answer ends, in the run that composed it.</strong> A turn's messages are then all written,
/// so they are placed in one call rather than one call per message, and nothing is added to the time a person waits for
/// an answer to start. A stopped answer is placed as far as it got.
/// </para>
/// <para>
/// <strong>Nothing placed is nothing lost.</strong> A deployment with no active profile, a provider that cannot be
/// reached, or a model that no longer matches the stored vectors leaves the turn unembedded, and a conversation with no
/// vector is still found by its words. That is also why nothing is retried: a turn that missed its vectors ranks
/// lexically, which is a difference in quality rather than a gap.
/// </para>
/// </remarks>
public sealed class AgentConversationEmbedding
{
    private readonly IAgentConversationStore store;
    private readonly ActiveEmbeddingSpace space;
    private readonly IAgentConversationSearchIndex index;

    /// <summary>Initializes the embedding of conversation turns.</summary>
    /// <param name="store">Reads the turn back as it was written.</param>
    /// <param name="space">Places its messages beside the stored vectors, or reports that it cannot.</param>
    /// <param name="index">Records where each message was placed.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public AgentConversationEmbedding(
        IAgentConversationStore store,
        ActiveEmbeddingSpace space,
        IAgentConversationSearchIndex index)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(index);

        this.store = store;
        this.space = space;
        this.index = index;
    }

    /// <summary>Embeds the question a run answered and everything said in its answer.</summary>
    /// <param name="question">The question whose turn has ended.</param>
    /// <param name="cancellationToken">Cancels the reading, the placing, and the recording.</param>
    /// <returns>A task that completes once every message the turn holds is recorded, or once it is clear none will be.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="question" /> is <see langword="null" />.</exception>
    public async Task EmbedTurnAsync(AgentQuestion question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);

        var said = await this.ReadSaidAsync(question, cancellationToken);
        if (said.Count is 0)
        {
            return;
        }

        var placement = await this.space.PlaceAsync([.. said.Select(static message => message.Text)], cancellationToken);
        if (placement is not { Profile: { } profile, Vectors: { } vectors })
        {
            return;
        }

        foreach (var (message, vector) in said.Zip(vectors))
        {
            await this.index.SaveEmbeddingAsync(
                question.Conversation,
                question.User,
                message.Sequence,
                profile,
                vector,
                cancellationToken);
        }
    }

    /// <summary>Picks out what was said in one entry of the turn, or nothing where the entry says nothing to embed.</summary>
    private static Said? SaidIn(AgentConversationEntry entry, AgentMessageId answer) => entry switch
    {
        AgentMessageWritten { Author: AgentMessageAuthor.Person } written => new Said(entry.Sequence, written.Text.Value),
        AgentBlockComposed { Block: AnswerBlock prose } composed when composed.MessageId == answer =>
            new Said(entry.Sequence, prose.Text.Value),
        _ => null,
    };

    /// <summary>Reads the turn from its question to its answer's ending, keeping what was said in it.</summary>
    /// <remarks>
    /// The question sits in the place just before the one its answer opened at, both having been written by one
    /// statement. Reading stops at the answer's ending, because whatever follows it belongs to the next question.
    /// </remarks>
    private async Task<List<Said>> ReadSaidAsync(AgentQuestion question, CancellationToken cancellationToken)
    {
        List<Said> said = [];
        var after = question.OpenedAt - 2;
        AgentConversationReading? reading;

        do
        {
            reading = await this.store.ReadAsync(
                question.Conversation,
                question.User,
                AgentConversationHistory.Visible,
                after,
                AgentConversationBounds.MaximumEntriesPerRead,
                cancellationToken);

            if (reading is null || reading.Entries.Count is 0)
            {
                break;
            }

            var turn = reading.Entries
                .TakeWhile(entry => entry is not AgentAnswerEnded ended || ended.MessageId != question.Answer)
                .ToArray();

            said.AddRange(turn
                .Select(entry => SaidIn(entry, question.Answer))
                .OfType<Said>()
                .Where(static message => !string.IsNullOrWhiteSpace(message.Text)));

            if (turn.Length < reading.Entries.Count)
            {
                break;
            }

            after = reading.Entries[^1].Sequence;
        }
        while (reading.MoreFollows);

        return said;
    }

    /// <summary>One message's text and the place of the entry it was read from.</summary>
    private sealed record Said(long Sequence, string Text);
}

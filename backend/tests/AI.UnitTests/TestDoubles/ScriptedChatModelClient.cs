// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Chat;

namespace MailFathom.AI.UnitTests.TestDoubles;

/// <summary>Answers the chat port from a script keyed by what a conversation carries, so a caller that judges several things at once is decidable.</summary>
/// <remarks>
/// <para>
/// Keyed by a marker found in the last turn rather than by call order, because the caller this stands in for sends its
/// conversations together: a queue of answers would hand each one to whichever call happened to reach it first, and the
/// test would state an expectation the run has no way to meet.
/// </para>
/// <para>
/// It records every conversation it was sent, which is what lets a test read the instruction and the envelope that
/// actually left the caller instead of asserting against a copy of them. A turn's picture is the one part it does copy,
/// for the reason <see cref="Recordable" /> gives.
/// </para>
/// </remarks>
internal sealed class ScriptedChatModelClient : IChatModelClient
{
    private readonly Lock gate = new();
    private readonly List<IReadOnlyList<ChatMessage>> conversations = [];
    private readonly Dictionary<string, ChatGenerationFailure?> scriptByMarker = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Text, ChatGenerationStop Stop)> answerByMarker = new(StringComparer.Ordinal);
    private string? answerForEverythingElse;

    /// <summary>Gets every conversation this client was sent, in the order it received them.</summary>
    public IReadOnlyList<IReadOnlyList<ChatMessage>> Conversations
    {
        get
        {
            lock (this.gate)
            {
                return [.. this.conversations];
            }
        }
    }

    /// <summary>Gets how many conversations this client was sent.</summary>
    public int CallCount => this.Conversations.Count;

    /// <summary>Arranges what the model answers for a conversation naming one marker.</summary>
    /// <param name="marker">Text the conversation's last turn carries, which for a judgement is the message identifier.</param>
    /// <param name="answerText">What the model answers.</param>
    /// <param name="stop">Why the model stopped, which a caller distinguishing a truncated or withheld answer from a finished one reads.</param>
    /// <returns>This client, so arrangement reads as one statement.</returns>
    public ScriptedChatModelClient Answering(
        string marker,
        string answerText,
        ChatGenerationStop stop = ChatGenerationStop.Completed)
    {
        this.answerByMarker[marker] = (answerText, stop);
        this.scriptByMarker[marker] = null;

        return this;
    }

    /// <summary>Arranges that a conversation naming one marker fails instead of answering.</summary>
    /// <param name="marker">Text the conversation's last turn carries.</param>
    /// <param name="failure">What kind of failure ends the call.</param>
    /// <returns>This client, so arrangement reads as one statement.</returns>
    public ScriptedChatModelClient Failing(string marker, ChatGenerationFailure failure)
    {
        this.scriptByMarker[marker] = failure;

        return this;
    }

    /// <summary>Arranges what the model answers for a conversation naming no arranged marker.</summary>
    /// <param name="answerText">What the model answers.</param>
    /// <returns>This client, so arrangement reads as one statement.</returns>
    public ScriptedChatModelClient AnsweringEverythingElse(string answerText)
    {
        this.answerForEverythingElse = answerText;

        return this;
    }

    /// <inheritdoc />
    public Task<ChatAnswer> AnswerAsync(IReadOnlyList<ChatMessage> conversation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        cancellationToken.ThrowIfCancellationRequested();

        lock (this.gate)
        {
            this.conversations.Add(Recordable(conversation));
        }

        var lastTurn = conversation[^1].Text;
        var marker = this.scriptByMarker.Keys.FirstOrDefault(candidate => lastTurn.Contains(candidate, StringComparison.Ordinal));

        if (marker is not null && this.scriptByMarker[marker] is { } failure)
        {
            throw new ChatGenerationFailedException("judging", failure);
        }

        var answer = marker is not null
            ? this.answerByMarker[marker]
            : (Text: this.answerForEverythingElse
                    ?? throw new InvalidOperationException("The script names no answer for this conversation."),
                Stop: ChatGenerationStop.Completed);

        return Task.FromResult(new ChatAnswer(answer.Text, answer.Stop, Usage: null));
    }

    /// <summary>Copies a turn's picture out of whatever the caller lent it, so a recorded conversation outlives the call.</summary>
    /// <remarks>
    /// A caller reading an attachment into a pooled buffer hands this port a window of it and returns the buffer as
    /// soon as the call completes, which is the right thing for it to do and the wrong thing for a test to assert
    /// against afterwards: the octets would be read out of a buffer somebody else may already have rented. The text is
    /// a string and needs none of this.
    /// </remarks>
    private static IReadOnlyList<ChatMessage> Recordable(IReadOnlyList<ChatMessage> conversation) =>
    [
        .. conversation.Select(static turn => turn.Image is { } image
            ? turn with { Image = image with { Content = image.Content.ToArray() } }
            : turn),
    ];
}

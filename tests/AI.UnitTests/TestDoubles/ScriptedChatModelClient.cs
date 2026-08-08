// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
/// actually left the caller instead of asserting against a copy of them.
/// </para>
/// </remarks>
internal sealed class ScriptedChatModelClient : IChatModelClient
{
    private readonly Lock gate = new();
    private readonly List<IReadOnlyList<ChatMessage>> conversations = [];
    private readonly Dictionary<string, ChatGenerationFailure?> scriptByMarker = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> answerByMarker = new(StringComparer.Ordinal);
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
    /// <returns>This client, so arrangement reads as one statement.</returns>
    public ScriptedChatModelClient Answering(string marker, string answerText)
    {
        this.answerByMarker[marker] = answerText;
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
            this.conversations.Add(conversation);
        }

        var lastTurn = conversation[^1].Text;
        var marker = this.scriptByMarker.Keys.FirstOrDefault(candidate => lastTurn.Contains(candidate, StringComparison.Ordinal));

        if (marker is not null && this.scriptByMarker[marker] is { } failure)
        {
            throw new ChatGenerationFailedException("judging", failure);
        }

        var answerText = marker is not null
            ? this.answerByMarker[marker]
            : this.answerForEverythingElse
                ?? throw new InvalidOperationException("The script names no answer for this conversation.");

        return Task.FromResult(new ChatAnswer(answerText, ChatGenerationStop.Completed, Usage: null));
    }
}

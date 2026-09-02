// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Retrieval;
using Microsoft.Extensions.AI;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>A chat provider that follows a written script, so a whole answering run reaches a real mailbox at no cost.</summary>
/// <remarks>
/// <para>
/// The one substitute in the answering tests, and deliberately the only one: everything under it — the database, the
/// two rankings, the search use case, the retrieval, the tool the run offers, the run ledger, and the published result —
/// is the deployment's own. What a model would decide is the one thing a test has to state rather than discover, and
/// stating it is what makes a run reproducible.
/// </para>
/// <para>
/// It is the framework's own <see cref="IChatClient" /> rather than a port of this repository's, because that is
/// precisely what the composition hands the agent. Substituting anything else would prove something about a copy of the
/// seam instead of the seam.
/// </para>
/// </remarks>
internal sealed class ScriptedAnsweringChatClient : IChatClient
{
    private readonly Queue<AnsweringTurn> turns;
    private readonly List<IReadOnlyList<ChatMessage>> conversations = [];

    private ScriptedAnsweringChatClient(IEnumerable<AnsweringTurn> turns) =>
        this.turns = new Queue<AnsweringTurn>(turns);

    /// <summary>Gets the conversation each turn of the run was asked to send, in order.</summary>
    /// <remarks>The last of them holds every tool result the run received, which is where a test reads what the mailbox handed the model.</remarks>
    public IReadOnlyList<IReadOnlyList<ChatMessage>> Conversations => this.conversations;

    /// <summary>Builds a client that answers straight away, looking nothing up.</summary>
    /// <param name="answerText">What to answer.</param>
    /// <returns>The client.</returns>
    public static ScriptedAnsweringChatClient Answering(string answerText) =>
        new([AnsweringTurn.Answer(answerText)]);

    /// <summary>Builds a client that makes one lookup and then answers.</summary>
    /// <param name="lookup">The arguments to call the search tool with.</param>
    /// <param name="answerText">What to answer once the lookup has been answered.</param>
    /// <returns>The client.</returns>
    public static ScriptedAnsweringChatClient LookingUp(
        IReadOnlyDictionary<string, object?> lookup,
        string answerText) =>
        new([AnsweringTurn.LookUp(lookup), AnsweringTurn.Answer(answerText)]);

    /// <summary>Builds a client that makes several lookups in order and then answers.</summary>
    /// <param name="lookups">The arguments each lookup is called with, in the order the run makes them.</param>
    /// <param name="answerText">What to answer once every lookup has been answered.</param>
    /// <returns>The client.</returns>
    public static ScriptedAnsweringChatClient LookingUpSeveralTimes(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> lookups,
        string answerText) =>
        new([.. lookups.Select(AnsweringTurn.LookUp), AnsweringTurn.Answer(answerText)]);

    /// <summary>Builds the arguments of one lookup, naming only what it narrows by.</summary>
    /// <param name="queryText">The text to rank mail against.</param>
    /// <param name="senderAddress">The sender to narrow to, or <see langword="null" /> to name none.</param>
    /// <param name="subjectFragment">The subject text to narrow to, or <see langword="null" /> to name none.</param>
    /// <param name="receivedOnOrAfter">The inclusive start of the received range, or <see langword="null" /> to name none.</param>
    /// <param name="receivedBefore">The exclusive end of the received range, or <see langword="null" /> to name none.</param>
    /// <param name="hasAttachments">Whether attachments are required, or <see langword="null" /> to name none.</param>
    /// <returns>The arguments, holding an entry only for each value the lookup named.</returns>
    /// <remarks>
    /// An argument nobody named is absent rather than null, because that is what a model writing a narrow lookup
    /// produces and what the tool's own defaults are written for.
    /// </remarks>
    public static IReadOnlyDictionary<string, object?> Lookup(
        string queryText,
        string? senderAddress = null,
        string? subjectFragment = null,
        DateTimeOffset? receivedOnOrAfter = null,
        DateTimeOffset? receivedBefore = null,
        bool? hasAttachments = null)
    {
        Dictionary<string, object?> arguments = new(StringComparer.Ordinal)
        {
            [ScopedMailKnowledgeRetrieval.QueryArgumentName] = queryText,
        };

        Name(arguments, "senderAddress", senderAddress);
        Name(arguments, "subjectFragment", subjectFragment);
        Name(arguments, "receivedOnOrAfter", receivedOnOrAfter?.ToString("O"));
        Name(arguments, "receivedBefore", receivedBefore?.ToString("O"));
        Name(arguments, "hasAttachments", hasAttachments);

        return arguments;
    }

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        this.conversations.Add([.. messages]);

        if (this.turns.Count is 0)
        {
            throw new InvalidOperationException("The script names no further turn.");
        }

        var turn = this.turns.Dequeue();

        return Task.FromResult(turn.Lookup is { } lookup
            ? LookupResponse(lookup, options, this.conversations.Count)
            : new ChatResponse(new ChatMessage(ChatRole.Assistant, turn.AnswerText))
            {
                FinishReason = ChatFinishReason.Stop,
            });
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The script answers whole responses only.");

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    /// <inheritdoc />
    public void Dispose()
    {
        // Nothing is held.
    }

    /// <summary>Turns a scripted lookup into a call on the tool the run actually offered.</summary>
    /// <remarks>
    /// The offered tool is found by name rather than taken on trust, so a script naming a tool the composition stopped
    /// offering fails here instead of quietly answering from a run that retrieved nothing.
    /// </remarks>
    private static ChatResponse LookupResponse(
        IReadOnlyDictionary<string, object?> lookup,
        ChatOptions? options,
        int turnNumber)
    {
        var tool = options?.Tools?
            .OfType<AIFunction>()
            .FirstOrDefault(offered => offered.Name == ScopedMailKnowledgeRetrieval.SearchToolName)
            ?? throw new InvalidOperationException(
                $"The run offered no tool named '{ScopedMailKnowledgeRetrieval.SearchToolName}'.");

        return new ChatResponse(new ChatMessage(
            ChatRole.Assistant,
            [new FunctionCallContent($"call-{turnNumber}", tool.Name, new Dictionary<string, object?>(lookup))]));
    }

    private static void Name(Dictionary<string, object?> arguments, string argumentName, object? value)
    {
        if (value is not null)
        {
            arguments[argumentName] = value;
        }
    }

    /// <summary>One turn of the script: either a lookup to make or the answer to end on.</summary>
    /// <param name="Lookup">The arguments of the lookup to make, or <see langword="null" /> when this turn answers.</param>
    /// <param name="AnswerText">What to answer, which is read only when <paramref name="Lookup" /> is absent.</param>
    private sealed record AnsweringTurn(IReadOnlyDictionary<string, object?>? Lookup, string AnswerText)
    {
        public static AnsweringTurn LookUp(IReadOnlyDictionary<string, object?> lookup) => new(lookup, string.Empty);

        public static AnsweringTurn Answer(string answerText) => new(Lookup: null, answerText);
    }
}

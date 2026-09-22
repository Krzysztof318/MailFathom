// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Domain.Emails;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.AgentConversations;

/// <summary>Writes every step of a run's tool loop into the conversation, so what each call sent can be rebuilt from the record.</summary>
/// <remarks>
/// <para>
/// It sits beneath the framework's tool loop, which calls it once per turn: a tool the model asks for arrives in the
/// answer to one call and its result is sent with the next, so each is written the moment this client first sees it —
/// the call as the answer comes back, the result as the call carrying it goes out. Every tool call and every tool result
/// is therefore in the conversation's own order, in the order the loop ran them, and belongs to the technical history
/// alone: a person sees what the run did through its status line, never through these.
/// </para>
/// <para>
/// It also records what the run's first call sent and was charged, which is the rate the next turn's budget is measured
/// at. The first call is the one whose input is the conversation itself; the later ones add this run's own tool traffic,
/// so a first call the provider reported no usage for leaves no charge rather than letting a later one stand in for it.
/// </para>
/// <para>
/// A write the conversation refuses is the stop reaching the run, exactly as it is for everything else a run writes, so
/// the call is abandoned rather than sent.
/// </para>
/// </remarks>
internal sealed class RecordedChatClient : DelegatingChatClient
{
    /// <summary>The provider's own serialization of a call's arguments and a tool's result, written without indentation, which the record would otherwise pay for in every entry.</summary>
    private static readonly JsonSerializerOptions RecordedJson = new(AIJsonUtilities.DefaultOptions) { WriteIndented = false };

    private readonly AgentAnswerJournal journal;
    private readonly HashSet<string> answered = [];
    private bool called;

    /// <summary>Initializes the client over the one the run sends through.</summary>
    /// <param name="innerClient">The client every call is sent through.</param>
    /// <param name="journal">Where the run's steps are written.</param>
    internal RecordedChatClient(IChatClient innerClient, AgentAnswerJournal journal)
        : base(innerClient)
    {
        this.journal = journal;
    }

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ChatMessage[] sent = [.. messages];

        foreach (var result in sent.SelectMany(static message => message.Contents).OfType<FunctionResultContent>())
        {
            if (this.answered.Add(result.CallId))
            {
                await this.RequireAsync(this.journal.RecordToolResultAsync(result.CallId, Recorded(Serialized(result.Result)), cancellationToken));
            }
        }

        var response = await base.GetResponseAsync(sent, options, cancellationToken);

        var first = !this.called;
        this.called = true;

        if (first && response.Usage?.InputTokenCount is > 0 and var inputTokens)
        {
            await this.RequireAsync(this.journal.RecordChargeAsync(CharactersOf(sent, options), inputTokens, cancellationToken));
        }

        foreach (var call in response.Messages.SelectMany(static message => message.Contents).OfType<FunctionCallContent>())
        {
            await this.RequireAsync(this.journal.RecordToolCallAsync(call.CallId, call.Name, Recorded(Serialized(call.Arguments)), cancellationToken));
        }

        return response;
    }

    /// <inheritdoc />
    /// <remarks>Releases nothing, for the reason <see cref="SteeredChatClient" /> gives: the client beneath it is the run's budgeted one, which its owner releases asynchronously.</remarks>
    protected override void Dispose(bool disposing) => base.Dispose(disposing: false);

    /// <summary>Writes a value the way it travels to the provider, as a JSON document.</summary>
    private static string Serialized(object? value) =>
        JsonSerializer.Serialize(value, RecordedJson);

    /// <summary>Cuts a recorded text to the length the record keeps of one tool call or result.</summary>
    private static string Recorded(string text) =>
        text.Length <= AgentConversationBounds.MaximumToolTextLength
            ? text
            : MailTextBounds.TruncateAtTextElementBoundary(text, AgentConversationBounds.MaximumToolTextLength);

    /// <summary>Counts the characters of text a call sends: the instruction, every message, and the tools it may call.</summary>
    /// <remarks>
    /// The tools' descriptions and schemas are counted because the provider charges for them, so a rate measured
    /// without them would read a short conversation as costing several times what it does.
    /// </remarks>
    private static long CharactersOf(IReadOnlyList<ChatMessage> sent, ChatOptions? options) =>
        (options?.Instructions?.Length ?? 0)
        + sent.SelectMany(static message => message.Contents).Sum(static content => (long)(content switch
        {
            TextContent text => text.Text.Length,
            FunctionCallContent call => call.Name.Length + Serialized(call.Arguments).Length,
            FunctionResultContent result => Serialized(result.Result).Length,
            _ => 0,
        }))
        + (options?.Tools ?? []).OfType<AIFunctionDeclaration>().Sum(static tool =>
            (long)tool.Name.Length + tool.Description.Length + tool.JsonSchema.GetRawText().Length);

    private async Task RequireAsync(Task<bool> write)
    {
        if (!await write)
        {
            this.journal.Stopping.ThrowIfCancellationRequested();
        }
    }
}

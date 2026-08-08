// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.AI;

namespace MailFathom.AI.UnitTests.TestDoubles;

/// <summary>A chat client that answers from a script, so a composed agent can be run without a provider.</summary>
/// <remarks>
/// <para>
/// The framework's own interface rather than a port of this repository's, because what the composition hands the agent
/// is exactly this: substituting anything else would prove something about a copy of the seam instead of the seam.
/// </para>
/// <para>
/// It records what each turn was asked to send, which is what lets a test assert the tools the agent offered and the
/// generation parameters it carried without reaching a network.
/// </para>
/// </remarks>
internal sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<ChatResponse> answers;
    private readonly List<ChatCall> calls = [];

    private ScriptedChatClient(IEnumerable<ChatResponse> answers) => this.answers = new Queue<ChatResponse>(answers);

    /// <summary>Gets what each turn of the run was asked to send, in order.</summary>
    public IReadOnlyList<ChatCall> Calls => this.calls;

    /// <summary>Builds a client that answers with text and asks for nothing.</summary>
    public static ScriptedChatClient Answering(string text) =>
        new([new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { FinishReason = ChatFinishReason.Stop }]);

    /// <summary>Builds a client that answers with text and reports what the call consumed.</summary>
    /// <param name="text">What to answer.</param>
    /// <param name="inputTokens">The tokens the provider reports the conversation occupied.</param>
    /// <param name="outputTokens">The tokens the provider reports the answer occupied.</param>
    public static ScriptedChatClient AnsweringWithUsage(string text, long inputTokens, long outputTokens) =>
        new([
            new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
            {
                FinishReason = ChatFinishReason.Stop,
                Usage = new UsageDetails { InputTokenCount = inputTokens, OutputTokenCount = outputTokens },
            },
        ]);

    /// <summary>Builds a client that calls one tool with one argument and then answers with text.</summary>
    /// <param name="toolName">The tool to call, which the run must have offered.</param>
    /// <param name="argument">The single argument to call it with, whose name is read from the offered tool's schema.</param>
    /// <param name="text">What to answer once the tool has answered.</param>
    /// <remarks>
    /// The argument name is discovered rather than written down, because it is the framework's reading of a delegate's
    /// parameter and a test that hard-coded it would fail on a framework update for a reason unrelated to what it proves.
    /// </remarks>
    public static ScriptedChatClient CallingTool(string toolName, string argument, string text)
    {
        var client = new ScriptedChatClient([]);

        client.answers.Enqueue(new ChatResponse(new ChatMessage(
            ChatRole.Assistant,
            [new ToolCallPlaceholder(toolName, argument)])));
        client.answers.Enqueue(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            FinishReason = ChatFinishReason.Stop,
        });

        return client;
    }

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        this.calls.Add(new ChatCall([.. messages], options));

        if (this.answers.Count is 0)
        {
            throw new InvalidOperationException("The script names no further answer.");
        }

        return Task.FromResult(Resolved(this.answers.Dequeue(), options));
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

    /// <summary>Turns a placeholder into the call the offered tool actually accepts.</summary>
    private static ChatResponse Resolved(ChatResponse answer, ChatOptions? options)
    {
        if (answer.Messages[0].Contents is not [ToolCallPlaceholder placeholder])
        {
            return answer;
        }

        var tool = options?.Tools?.OfType<AIFunction>().FirstOrDefault(offered => offered.Name == placeholder.ToolName)
            ?? throw new InvalidOperationException(
                $"The run offered no tool named '{placeholder.ToolName}' for the script to call.");

        return new ChatResponse(new ChatMessage(
            ChatRole.Assistant,
            [
                new FunctionCallContent(
                    "call-1",
                    tool.Name,
                    new Dictionary<string, object?> { [SoleParameterNameOf(tool)] = placeholder.Argument }),
            ]));
    }

    /// <summary>Reads the name of the one argument the offered tool takes.</summary>
    private static string SoleParameterNameOf(AIFunction tool)
    {
        var properties = tool.JsonSchema.GetProperty("properties").EnumerateObject().ToArray();

        return properties.Length is 1
            ? properties[0].Name
            : throw new InvalidOperationException(
                $"Tool '{tool.Name}' takes {properties.Length} arguments, and the script calls tools that take one.");
    }

    /// <summary>What one turn of a run was asked to send.</summary>
    /// <param name="Messages">The conversation as the agent had composed it by that turn.</param>
    /// <param name="Options">The options the agent carried, including the tools it offered.</param>
    internal sealed record ChatCall(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options);

    /// <summary>Stands in for a tool call until the offered tool's own argument name is known.</summary>
    private sealed class ToolCallPlaceholder(string toolName, string argument) : AIContent
    {
        public string ToolName { get; } = toolName;

        public string Argument { get; } = argument;
    }
}

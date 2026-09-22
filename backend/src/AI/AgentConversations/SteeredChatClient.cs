// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Agent.Answering;
using MailFathom.Application.SensitiveContent.Egress;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.AgentConversations;

/// <summary>Adds what the person wrote into a run while it composes to the model's next turn.</summary>
/// <remarks>
/// <para>
/// Steering interrupts nothing. An instruction posted into a run is read from the conversation before each call the run
/// makes, and every one read so far is sent after everything the run has already exchanged — so the model meets it at
/// its next turn, with every tool result it already has still in front of it.
/// </para>
/// <para>
/// It sits beneath the framework's tool loop rather than above it, which is what makes <em>next turn</em> mean the next
/// call to the model rather than the next run: the loop calls this client once per turn, and a run that reads three
/// messages before answering is steered between any two of them. An instruction is the person's own text, so it is
/// scanned like every other text this deployment sends.
/// </para>
/// </remarks>
internal sealed class SteeredChatClient : DelegatingChatClient
{
    private readonly AgentAnswerJournal journal;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly List<ChatMessage> instructions = [];

    /// <summary>Initializes the client over the one the run sends through.</summary>
    /// <param name="innerClient">The client every call is sent through.</param>
    /// <param name="journal">Where the run's instructions are read from.</param>
    /// <param name="egressGuard">Scans each instruction before it is sent.</param>
    internal SteeredChatClient(IChatClient innerClient, AgentAnswerJournal journal, SensitiveContentEgressGuard egressGuard)
        : base(innerClient)
    {
        this.journal = journal;
        this.egressGuard = egressGuard;
    }

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var instruction in await this.journal.ReadInstructionsAsync(cancellationToken))
        {
            var guarded = await this.egressGuard.GuardAsync(SensitiveContentEgressPoint.ChatPrompt, instruction.Value, cancellationToken);

            this.instructions.Add(new ChatMessage(ChatRole.User, guarded));
        }

        return await base.GetResponseAsync([.. messages, .. this.instructions], options, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Releases nothing, because it owns nothing: the client beneath it is the run's budgeted one, whose owner releases
    /// it asynchronously so the spend it still holds is charged, and a synchronous release cascading into it from here
    /// would be refused rather than charged.
    /// </remarks>
    protected override void Dispose(bool disposing) => base.Dispose(disposing: false);
}

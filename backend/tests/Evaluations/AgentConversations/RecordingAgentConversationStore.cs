// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Domain.Access;

namespace MailFathom.Evaluations.AgentConversations;

/// <summary>The one conversation a scenario's run writes into, kept in memory so what the run wrote can be read back.</summary>
/// <remarks>
/// A run appends and reads for steering, and nothing else; every other statement belongs to a route a scenario has no
/// request for, so it refuses rather than pretending to a database.
/// </remarks>
internal sealed class RecordingAgentConversationStore : IAgentConversationStore
{
    private readonly ConcurrentQueue<AgentConversationEntry> written = new();

    /// <summary>Gets every entry the run wrote, in the order it wrote them.</summary>
    public IReadOnlyList<AgentConversationEntry> Written => [.. this.written];

    /// <inheritdoc />
    public Task<long?> AppendAsync(
        AgentConversationId id,
        UserId user,
        AgentConversationEntry entry,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        this.written.Enqueue(entry);

        return Task.FromResult<long?>(this.written.Count);
    }

    /// <inheritdoc />
    /// <remarks>Answers a conversation still composing and holding nothing new, since nobody steers a scenario.</remarks>
    public Task<AgentConversationReading?> ReadAsync(
        AgentConversationId id,
        UserId user,
        AgentConversationHistory history,
        long afterSequence,
        int limit,
        CancellationToken cancellationToken) =>
        Task.FromResult<AgentConversationReading?>(new(Title: null, DateTimeOffset.UnixEpoch, Composing: true, [], MoreFollows: false));

    /// <inheritdoc />
    public Task<bool> TryStartAsync(AgentConversationId id, UserId user, DateTimeOffset now, CancellationToken cancellationToken) =>
        throw Unasked();

    /// <inheritdoc />
    public Task<long?> StopAsync(
        AgentConversationId id,
        UserId user,
        AgentMessageId answer,
        AgentMessageWritten note,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        throw Unasked();

    /// <inheritdoc />
    public Task<AgentMessagePosting> AskAsync(
        AgentConversationId id,
        UserId user,
        AgentMessageWritten question,
        AgentMessageId answer,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        throw Unasked();

    /// <inheritdoc />
    public Task<AgentMessagePosting> SteerAsync(
        AgentConversationId id,
        UserId user,
        AgentMessageId answer,
        AgentMessageWritten instruction,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        throw Unasked();

    /// <inheritdoc />
    public Task<long?> TryResolveProposalAsync(
        AgentConversationId id,
        UserId user,
        long proposedAt,
        AgentProposalState state,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        throw Unasked();

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentConversationSummary>> ListAsync(UserId user, int limit, CancellationToken cancellationToken) =>
        throw Unasked();

    /// <inheritdoc />
    public Task<bool> TrySetTitleAsync(AgentConversationId id, UserId user, PresentationText title, CancellationToken cancellationToken) =>
        throw Unasked();

    /// <inheritdoc />
    public Task<bool> TryDeleteAsync(AgentConversationId id, UserId user, CancellationToken cancellationToken) =>
        throw Unasked();

    private static NotSupportedException Unasked() => new("A scenario's run only appends and reads.");
}

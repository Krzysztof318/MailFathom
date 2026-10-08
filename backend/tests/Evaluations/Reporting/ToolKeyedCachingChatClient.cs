// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;

namespace MailFathom.Evaluations.Reporting;

/// <summary>A response cache that files an answer under the tools the model was offered as well as under the request, and tallies where each answer came from.</summary>
/// <param name="model">The model under test.</param>
/// <param name="cache">The cache the answers live in.</param>
/// <param name="writtenKeys">The keys this run has written to <paramref name="cache" />, shared by every client over it.</param>
/// <param name="tally">Counts each answer as replayed from an earlier run, reused from this one, or asked of the model.</param>
/// <remarks>
/// <para>
/// <see cref="ChatOptions.Tools" /> is left out of the JSON the library hashes into a key, so a changed tool name,
/// description, or parameter schema would otherwise read back the answer the model gave to the old definitions. A request
/// offered no tools hands the library exactly the values it would hash without this type, so its key is unchanged.
/// </para>
/// <para>
/// An answer read back is told apart by whether this run wrote its entry, because a scenario asking one question under
/// several settings reads its own fresh answer back by design, and counting that as a replay would make a run that
/// measured everything read as one that measured a fraction of it.
/// </para>
/// </remarks>
internal sealed class ToolKeyedCachingChatClient(
    IChatClient model,
    IDistributedCache cache,
    ConcurrentDictionary<string, byte> writtenKeys,
    CachedAnswerTally tally)
    : DistributedCachingChatClient(model, cache)
{
    /// <inheritdoc />
    protected override string GetCacheKey(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        params ReadOnlySpan<object?> additionalValues) =>
        options?.Tools is { Count: > 0 } tools
            ? base.GetCacheKey(messages, options, [.. additionalValues, .. tools.SelectMany(Identity)])
            : base.GetCacheKey(messages, options, additionalValues);

    /// <inheritdoc />
    protected override async Task<ChatResponse?> ReadCacheAsync(string key, CancellationToken cancellationToken)
    {
        var cached = await base.ReadCacheAsync(key, cancellationToken);

        if (cached is null)
        {
            tally.CountAsked();
        }
        else if (writtenKeys.ContainsKey(key))
        {
            tally.CountReused();
        }
        else
        {
            tally.CountReplayed();
        }

        return cached;
    }

    /// <inheritdoc />
    protected override async Task WriteCacheAsync(string key, ChatResponse value, CancellationToken cancellationToken)
    {
        await base.WriteCacheAsync(key, value, cancellationToken);

        writtenKeys.TryAdd(key, 0);
    }

    private static IEnumerable<object?> Identity(AITool tool) =>
        tool is AIFunctionDeclaration function
            ? [tool.Name, tool.Description, function.JsonSchema.GetRawText()]
            : [tool.Name, tool.Description];
}

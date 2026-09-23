// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;

namespace MailFathom.Evaluations.Reporting;

/// <summary>A response cache that files an answer under the tools the model was offered as well as under the request.</summary>
/// <param name="model">The model under test.</param>
/// <param name="cache">The cache the answers live in.</param>
/// <remarks>
/// <see cref="ChatOptions.Tools" /> is left out of the JSON the library hashes into a key, so a changed tool name,
/// description, or parameter schema would otherwise read back the answer the model gave to the old definitions. A request
/// offered no tools hands the library exactly the values it would hash without this type, so its key is unchanged.
/// </remarks>
internal sealed class ToolKeyedCachingChatClient(IChatClient model, IDistributedCache cache)
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

    private static IEnumerable<object?> Identity(AITool tool) =>
        tool is AIFunctionDeclaration function
            ? [tool.Name, tool.Description, function.JsonSchema.GetRawText()]
            : [tool.Name, tool.Description];
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.AI.Orchestration;

/// <summary>Finds the JSON object an agent was asked to answer with inside whatever it wrote around it.</summary>
/// <remarks>
/// A model told to answer with one object still fences it, prefaces it, or writes a sentence after it often enough that
/// treating any of those as a failed derivation would throw away a usable answer. The outermost braces are what is
/// read; anything either side of them is discarded unexamined.
/// </remarks>
internal static class AgentJsonAnswer
{
    private const string JsonFence = "```";

    /// <summary>Takes the fence and the prose off an answer, leaving the object or nothing.</summary>
    /// <param name="answerText">What the agent wrote, which may be empty, fenced, or surrounded by prose.</param>
    /// <returns>The JSON object, or <see langword="null" /> where the answer holds none.</returns>
    internal static string? Unfenced(string? answerText)
    {
        if (string.IsNullOrWhiteSpace(answerText))
        {
            return null;
        }

        var text = answerText.Replace(JsonFence, string.Empty, StringComparison.Ordinal);
        var opening = text.IndexOf('{', StringComparison.Ordinal);
        var closing = text.LastIndexOf('}');

        return opening >= 0 && closing > opening ? text[opening..(closing + 1)] : null;
    }
}

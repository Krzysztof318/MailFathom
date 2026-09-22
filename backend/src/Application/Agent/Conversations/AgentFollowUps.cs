// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;

namespace MailFathom.Application.Agent.Conversations;

/// <summary>What a question the agent suggests asking next has to be, stated once for the record and for whatever composes one.</summary>
/// <remarks>
/// A suggestion is pressed and then posted exactly as it reads, so it is held to what a person could have typed into the
/// field in one line: something to say, no line break, and short enough to read in full on the control that asks it.
/// </remarks>
public static class AgentFollowUps
{
    /// <summary>Gets whether one suggestion is a line a person could ask as it stands.</summary>
    /// <param name="followUp">The suggestion.</param>
    /// <returns><see langword="true" /> when it says something, in one line, within the length a suggestion may take.</returns>
    public static bool IsAskable(PresentationText followUp) =>
        followUp.IsSpecified
        && followUp.Value.Length <= AgentConversationBounds.MaximumFollowUpLength
        && !followUp.Value.Any(char.IsControl);

    /// <summary>Reads the suggestions a model wrote into the ones an answer may carry.</summary>
    /// <param name="written">What the model wrote, one suggestion per element.</param>
    /// <returns>The suggestions without repeats, in the order written; or <see langword="null" /> where none was written, more were written than an answer carries, or one is not askable as it stands.</returns>
    public static IReadOnlyList<PresentationText>? Read(IReadOnlyList<string>? written)
    {
        if (written is null or [] || written.Count > AgentConversationBounds.MaximumFollowUps)
        {
            return null;
        }

        List<PresentationText> read = [];

        foreach (var line in written)
        {
            if (!PresentationText.TryCreate(line, out var followUp) || !IsAskable(followUp))
            {
                return null;
            }

            read.Add(followUp);
        }

        return [.. read.DistinctBy(static followUp => followUp.Value, StringComparer.OrdinalIgnoreCase)];
    }
}

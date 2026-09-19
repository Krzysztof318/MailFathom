// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MailFathom.AI.Orchestration;

namespace MailFathom.Evaluations;

/// <summary>Reads the one JSON object an agent was told to answer with, as the model wrote it.</summary>
/// <remarks>
/// A deployment's own reading drops whatever cannot be resolved — a source nobody offered, a person the turn never
/// numbered — which is right for a person and blinds a measurement, because the dropped reference is exactly what a
/// scenario asserts the model never wrote. So a scenario reads the answer twice: through the deployment's reading for
/// what a person would be shown, and through this for what the model actually said. It finds the object where that
/// reading does, through <see cref="AgentJsonAnswer" />.
/// </remarks>
internal static class ModelJsonAnswer
{
    /// <summary>Reads the object out of an answer.</summary>
    /// <typeparam name="TDocument">The shape the agent answers in.</typeparam>
    /// <param name="answerText">What the agent wrote.</param>
    /// <param name="shape">The shape's serialization metadata, from the deployment's own source-generated context.</param>
    /// <returns>The object, or <see langword="null" /> where the answer carried none that reads as the shape.</returns>
    public static TDocument? Read<TDocument>(string? answerText, JsonTypeInfo<TDocument> shape)
        where TDocument : class
    {
        if (AgentJsonAnswer.Unfenced(answerText) is not { } json)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, shape);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Discovery.Presentation;

/// <summary>How far a synthesized answer is worth trusting beyond the sources it names.</summary>
/// <remarks>
/// Three bands rather than a number. A model's numeric self-assessment reads as a measurement and is not one, and a
/// screen drawing 0.82 invites arithmetic on it; three bands say what can honestly be said and are what a client has
/// distinct ways to draw. Confidence is about the synthesis — how much of the answer the sources actually settle —
/// while <see cref="PresentationSupport" /> is about the sources themselves, which is why a block carries both.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<PresentationConfidence>))]
public enum PresentationConfidence
{
    /// <summary>The sources settle the question, and the answer restates what they say.</summary>
    High = 0,

    /// <summary>The sources carry the answer, with a step of inference the reader may want to check.</summary>
    Moderate = 1,

    /// <summary>The answer is the best reading of partial sources and may be wrong.</summary>
    Low = 2,
}

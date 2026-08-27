// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Discovery.Presentation.Blocks;

/// <summary>The closed set of next steps a plan may suggest.</summary>
/// <remarks>
/// Closed for the reason the block catalogue is closed, and more sharply: an action is something a person may then
/// perform, so a set a model could add to would be a model naming a step nobody wrote a control for. Each member names
/// something the client already knows how to offer, and a step outside the set is not suggested at all.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<SuggestedActionKind>))]
public enum SuggestedActionKind
{
    /// <summary>Answer the conversation the answer came from.</summary>
    ReplyToThread = 0,

    /// <summary>Pass a message on to somebody else.</summary>
    ForwardEmail = 1,

    /// <summary>Write to somebody the answer named.</summary>
    ComposeEmail = 2,

    /// <summary>Mark a message so it is easy to come back to.</summary>
    FlagEmail = 3,

    /// <summary>Read the whole of a conversation the answer only quoted.</summary>
    OpenThread = 4,

    /// <summary>Search again with what the answer established, where the first question was narrower than the matter.</summary>
    SearchAgain = 5,

    /// <summary>Write a rule that files mail like this without being asked again.</summary>
    CreateMailRule = 6,
}

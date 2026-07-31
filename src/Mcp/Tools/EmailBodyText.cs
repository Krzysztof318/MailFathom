// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.ComponentModel;
using MailMcp.Application.EmailContent;

namespace MailMcp.Mcp.Tools;

/// <summary>Publishes one representation of a message body together with what was left out of it.</summary>
/// <remarks>
/// Truncation travels inside the representation rather than beside it, because a body and the fact that it is
/// incomplete are never useful apart: a model handed only the text would summarize a cut message as a whole one. Each
/// representation carries its own copy, since a message can exceed the bound in its plain text and not in its markup.
/// </remarks>
[Description("One representation of the message body, already bounded, stating how long the source was and whether anything was cut.")]
internal sealed record EmailBodyText
{
    /// <summary>Gets the representation as it is returned.</summary>
    [Description("The body text as returned, already bounded. Empty when the message displayed nothing in this representation.")]
    public required string Text { get; init; }

    /// <summary>Gets how many characters the source held before the bound was applied.</summary>
    [Description("How many characters this representation's source held before the bound was applied. Compare it with wasTruncated rather than with the length of text, which a re-serialized representation can change on its own.")]
    public required int OriginalCharacterCount { get; init; }

    /// <summary>Gets whether the bound removed anything.</summary>
    [Description("Whether the bound removed anything. When true the text ends mid-message, so state that the message continues rather than presenting it as complete.")]
    public required bool WasTruncated { get; init; }

    /// <summary>Publishes one bounded representation.</summary>
    /// <param name="representation">The representation the use case produced.</param>
    /// <returns>The wire representation of <paramref name="representation" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="representation" /> is <see langword="null" />.</exception>
    public static EmailBodyText From(EmailBodyRepresentation representation)
    {
        ArgumentNullException.ThrowIfNull(representation);

        return new EmailBodyText
        {
            Text = representation.Text,
            OriginalCharacterCount = representation.OriginalCharacterCount,
            WasTruncated = representation.WasTruncated,
        };
    }
}

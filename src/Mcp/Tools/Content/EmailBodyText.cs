// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.EmailContent.Rendering;

namespace MailFathom.Mcp.Tools.Content;

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
    [Description("How many characters this representation's source held before the bound was applied. Read truncatedBy rather than comparing this with the length of text, which a re-serialized representation can change on its own.")]
    public required int OriginalCharacterCount { get; init; }

    /// <summary>Gets which bound removed something, or that none did.</summary>
    [Description("Which bound cut the text: 'none' when it is the whole representation, 'bodyCharacterLimit' when this email alone is longer than one call returns, or 'readCharacterBudget' when the emails named before it had already spent the call's total budget. Anything other than 'none' means the text ends mid-message, so state that the message continues rather than presenting it as complete; 'readCharacterBudget' additionally means that naming fewer emails in one call returns more of this one.")]
    public required EmailBodyTruncationCause TruncatedBy { get; init; }

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
            TruncatedBy = PublishedCause(representation.Truncation),
        };
    }

    /// <summary>Reads the published value the application state names.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when an application state has no published value, which means one was added without deciding what a
    /// client should be told about it.
    /// </exception>
    private static EmailBodyTruncationCause PublishedCause(EmailBodyTruncation truncation) =>
        truncation switch
        {
            EmailBodyTruncation.None => EmailBodyTruncationCause.None,
            EmailBodyTruncation.BodyCharacterLimit => EmailBodyTruncationCause.BodyCharacterLimit,
            EmailBodyTruncation.ReadCharacterBudget => EmailBodyTruncationCause.ReadCharacterBudget,
            _ => throw new ArgumentOutOfRangeException(
                nameof(truncation),
                truncation,
                "The body truncation has no published protocol value."),
        };
}

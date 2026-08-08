// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;

namespace MailFathom.Application.Retrieval;

/// <summary>The question a caller asked about their mail, as it will reach a model.</summary>
/// <remarks>
/// <para>
/// A type of its own rather than a <see langword="string" /> parameter, for the reason the search query text is one: it
/// is untrusted text that leaves the process, so the bound and the refusal are stated once and a reader of the port
/// signature can see that what arrives there has already been checked.
/// </para>
/// <para>
/// The bound here is not the provider's. One chat call carries a declared character ceiling that an operator sets and
/// that covers the whole conversation, including the instruction and whatever the run retrieves; this covers the one
/// part a caller writes, and it is far below that ceiling so a question is refused where it was written rather than
/// after a run has been composed around it.
/// </para>
/// <para>
/// A question is personal data of the same revealing kind a search query is — what somebody wants to know about their
/// own mail — so it is never logged and no failure message repeats it.
/// </para>
/// </remarks>
public sealed record MailQuestionText
{
    /// <summary>The greatest number of characters a question may carry.</summary>
    /// <remarks>
    /// Generous against any question a person types and small enough that the question is never the reason a run's
    /// conversation exceeds what one call may send.
    /// </remarks>
    public const int MaximumLength = 1000;

    private MailQuestionText(string value) => this.Value = value;

    /// <summary>Gets the question as it will reach the model.</summary>
    public string Value { get; }

    /// <summary>Validates and normalizes the text a caller asked.</summary>
    /// <param name="text">The question a caller supplied.</param>
    /// <returns>The validated question.</returns>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when the text is blank, longer than <see cref="MaximumLength" />, or carries a control character.</exception>
    /// <remarks>
    /// Blank text is refused rather than sent. A run composed around no question would spend a provider call to be
    /// asked nothing, and whatever the model then wrote would be an answer to the instruction alone.
    /// </remarks>
    public static MailQuestionText Create(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw MailboxQueryFilterInvalidException.Blank("question");
        }

        var trimmed = text.Trim();

        MailboxQueryFilterInvalidException.ThrowIfLengthExceeded(trimmed.Length, MaximumLength, "question");

        // Refused for the reason a search query's control characters are, plus one this text adds: the question travels
        // to a provider inside a structured request, and a caller able to place control characters in it is a caller
        // writing into somebody else's parser.
        if (trimmed.Any(char.IsControl))
        {
            throw MailboxQueryFilterInvalidException.ContainsControlCharacter("question");
        }

        return new MailQuestionText(trimmed);
    }

    /// <inheritdoc />
    /// <remarks>Returns the length rather than the text, because a question is personal data and this is what a log or a debugger would show.</remarks>
    public override string ToString() => $"question of {this.Value.Length} characters";
}

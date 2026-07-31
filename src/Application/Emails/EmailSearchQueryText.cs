// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailFathom.Application.Emails;

/// <summary>The free text a caller is searching their mail for.</summary>
/// <remarks>
/// <para>
/// The text is never interpreted here. It is bounded and checked for characters no document could hold, and then
/// travels to PostgreSQL as one parameter that the full-text parser turns into a query — so a caller's operators,
/// quotes, and punctuation are data the parser reads rather than syntax anything composes with. Nothing in this type or
/// below it concatenates the value into SQL.
/// </para>
/// <para>
/// It is a type of its own rather than a <see langword="string" /> parameter because it is the one untrusted value that
/// reaches a query as text. A named type means the bound and the refusal are stated once, and a reader of the port
/// signature can see that the text arriving there is the validated one.
/// </para>
/// <para>
/// Query text is personal data of a particularly revealing kind — what somebody is looking for in their own mailbox —
/// so it is never logged and never repeated in a failure message.
/// </para>
/// </remarks>
public sealed record EmailSearchQueryText
{
    /// <summary>The greatest number of characters a search query may carry.</summary>
    /// <remarks>
    /// Generous against any phrase a person types and far below the point where the full-text parser's cost matters.
    /// Nothing about the query grammar bounds a length on its own, so without this a caller could send a megabyte of
    /// text that PostgreSQL would dutifully parse into a query no document can match.
    /// </remarks>
    public const int MaximumLength = 512;

    private EmailSearchQueryText(string value) => this.Value = value;

    /// <summary>Gets the query text as it will reach the full-text parser.</summary>
    public string Value { get; }

    /// <summary>Validates and normalizes the text a request asked to search for.</summary>
    /// <param name="text">The query text a caller supplied.</param>
    /// <returns>The validated query text.</returns>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when the text is blank, longer than <see cref="MaximumLength" />, or carries a control character.</exception>
    /// <remarks>
    /// Blank text is refused rather than treated as "match everything". A search with no text is a listing, which
    /// <see cref="ListEmails.MailboxTimelineReader" /> already answers in a stable order and with a cursor; answering it
    /// here would return an arbitrary relevance-ordered window of the whole mailbox instead, and every result would
    /// carry a rank of zero and a snippet of whatever each message happens to begin with.
    /// </remarks>
    public static EmailSearchQueryText Create(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw MailboxQueryFilterInvalidException.Blank("search query");
        }

        var trimmed = text.Trim();

        MailboxQueryFilterInvalidException.ThrowIfLengthExceeded(trimmed.Length, MaximumLength, "search query");

        // Refused for the reason a subject fragment's control characters are: PostgreSQL text cannot hold a zero byte,
        // so a query carrying one would surface as a provider exception instead of the failure this boundary publishes.
        if (trimmed.Any(char.IsControl))
        {
            throw MailboxQueryFilterInvalidException.ContainsControlCharacter("search query");
        }

        return new EmailSearchQueryText(trimmed);
    }

    /// <inheritdoc />
    /// <remarks>Returns the length rather than the text, because a query is mail-derived personal data and this is what a log or a debugger would show.</remarks>
    public override string ToString() => $"search query of {this.Value.Length} characters";
}

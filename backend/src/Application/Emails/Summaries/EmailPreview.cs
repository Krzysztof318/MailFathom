// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Summaries;

/// <summary>The bounded extract of a message's own text that a list row shows under its subject.</summary>
/// <remarks>
/// <para>
/// A preview is where a listing meets a body, so the bound is the data-minimization control of the whole read: a row
/// shows the opening of a message and never the message. Without it a page of fifty rows would be fifty bodies, which
/// is both a listing nobody asked for and the rule against reading raw MIME in an ordinary mailbox query written the
/// other way round.
/// </para>
/// <para>
/// The bound is fixed rather than configured and rather than asked for per request. A caller who could raise it could
/// lift the control that decides how much mail one page draws out, and a deployment that lowered it would publish a
/// different contract under the same route — while the useful value follows what a list row can show on a screen, which
/// is not a deployment's decision either.
/// </para>
/// </remarks>
public static class EmailPreview
{
    /// <summary>The greatest number of characters one preview carries.</summary>
    /// <remarks>
    /// About two lines of a list row. Long enough to tell two notifications from the same sender apart, and far short of
    /// reading the message, which is what a content read is for.
    /// </remarks>
    public const int MaximumCharacters = 200;

    /// <summary>Reduces the opening of a message's text to the one line a row draws.</summary>
    /// <param name="text">The message text as storage answered it, or <see langword="null" /> where nothing has extracted the message yet.</param>
    /// <returns>The preview, or <see langword="null" /> where the text is absent or carries nothing but whitespace.</returns>
    /// <remarks>
    /// Runs of whitespace collapse to one space, so a message whose opening is a wrapped quotation does not arrive as
    /// two hundred characters of line breaks. That makes a preview shorter than the bound rather than longer, which is
    /// why the cut belongs to the query and this belongs here: the query is what keeps a body out of this process, and
    /// reflowing what it returned cannot put one back. The bound is applied here as well, because a control that holds
    /// only while every query asks for the right number of characters is not a control.
    /// </remarks>
    public static string? Bounded(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return collapsed.Length <= MaximumCharacters ? collapsed : collapsed[..MaximumCharacters];
    }
}

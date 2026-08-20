// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Mcp.Tools.Results;

namespace MailFathom.Mcp.Tools.Drafts;

/// <summary>The message a caller described a draft with, in whichever of the two shapes it described it in.</summary>
/// <remarks>
/// <para>
/// Saving a draft and updating one take the same fields and differ in whether they name a draft to replace, so the
/// fields are one type both tools fill. That is what keeps <c>save_draft</c> and <c>update_draft</c> from drifting into
/// two spellings of one message — a subject required by one and optional by the other, an answer one of them can draft
/// and the other cannot.
/// </para>
/// <para>
/// Nothing is validated here. Every property is what a caller sent, and what makes a set of them a draft this system
/// can write is <see cref="DraftedMailWriting" />'s question.
/// </para>
/// </remarks>
internal sealed record DraftedMailFields
{
    /// <summary>Gets the account the draft belongs to, or <see langword="null" /> where the draft answers a stored email.</summary>
    public string? Account { get; init; }

    /// <summary>Gets the subject the author wrote, or <see langword="null" /> where the draft answers a stored email.</summary>
    public string? Subject { get; init; }

    /// <summary>Gets the plain-text body the author wrote, which every draft carries.</summary>
    public required string PlainTextBody { get; init; }

    /// <summary>Gets the HTML alternative the author wrote, or <see langword="null" /> when they wrote none.</summary>
    public string? HtmlBody { get; init; }

    /// <summary>Gets the addresses named in the <c>To</c> header.</summary>
    public IReadOnlyList<string>? To { get; init; }

    /// <summary>Gets the addresses named in the <c>Cc</c> header.</summary>
    public IReadOnlyList<string>? Cc { get; init; }

    /// <summary>Gets the addresses named in the <c>Bcc</c> header.</summary>
    public IReadOnlyList<string>? Bcc { get; init; }

    /// <summary>Gets the stored email the draft answers, or <see langword="null" /> where it answers none.</summary>
    public string? AnsweredEmailId { get; init; }

    /// <summary>Gets which answer the draft is, or <see langword="null" /> where it answers no stored email.</summary>
    public DraftedAnswer? Answers { get; init; }
}

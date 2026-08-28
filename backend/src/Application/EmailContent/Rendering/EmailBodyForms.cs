// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering;

/// <summary>Names the forms of its own body a message turned out to carry.</summary>
/// <param name="PlainText">Whether the message wrote a plain-text part of its own.</param>
/// <param name="Html">Whether the message wrote an HTML part, which is what a richer rendering is reduced from.</param>
/// <remarks>
/// <para>
/// It describes the message rather than the answer. Every read returns words whatever the message wrote — text is
/// derived from the markup where a sender offered no plain-text part — so what a reader cannot tell from a returned
/// representation is which of them the sender actually sent, and that is the question a screen choosing between a
/// reduced document and the words is really asking.
/// </para>
/// <para>
/// Both are false for a body nothing could read, which is a statement about a parse that never happened rather than
/// about a message that carried nothing: the availability beside it is what says which.
/// </para>
/// </remarks>
public sealed record EmailBodyForms(bool PlainText, bool Html)
{
    /// <summary>Gets the forms of a body nothing parsed.</summary>
    public static EmailBodyForms None { get; } = new(PlainText: false, Html: false);
}

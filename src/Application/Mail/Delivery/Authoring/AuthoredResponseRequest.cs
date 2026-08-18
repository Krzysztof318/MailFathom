// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Mail.Delivery.Authoring;

/// <summary>States what somebody is answering, how, and what they wrote of their own.</summary>
/// <remarks>
/// <para>
/// The answered message is named by its stable local identity and by nothing else. Everything read out of it — the
/// addresses, the subject, the threading identifiers, the quoted text, the files a forward carries — comes from the
/// stored copy that identity resolves to, so a caller cannot state any of them and cannot state them wrongly.
/// </para>
/// <para>
/// What an author does decide is here: which act it is, and the words they wrote. The recipients are theirs to add and
/// a forward's are theirs alone, because a forward goes to people the original never named.
/// </para>
/// </remarks>
public sealed record AuthoredResponseRequest
{
    /// <summary>Gets the stored email being answered.</summary>
    public required StoredEmailId AnsweredEmailId { get; init; }

    /// <summary>Gets which answer is being authored.</summary>
    public required AuthoredResponseAct Act { get; init; }

    /// <summary>Gets the plain-text the author wrote, which is placed above the quoted original.</summary>
    public required string PlainTextBody { get; init; }

    /// <summary>Gets the HTML alternative the author wrote, or <see langword="null" /> when they wrote none.</summary>
    /// <remarks>
    /// Its presence decides whether the answer carries an HTML alternative at all, and therefore whether the original
    /// is read a second way to be quoted in one. An author who wrote plain text alone sends plain text alone, exactly
    /// as they would for a message answering nothing.
    /// </remarks>
    public string? HtmlBody { get; init; }

    /// <summary>Gets the people the author named themselves, which is everybody a forward goes to.</summary>
    /// <remarks>
    /// <para>
    /// They are added to whoever the act itself addresses rather than replacing them, so naming somebody on a reply
    /// copies them in without dropping the person being answered. A forward addresses nobody of its own, so this is the
    /// whole of where it goes and a forward naming nobody is refused when it is composed.
    /// </para>
    /// <para>
    /// Each of them is named by an address or by somebody the contact book holds, exactly as they are on a message
    /// answering nothing. What the answer itself derives — whoever asked for answers, and everybody a reply to all keeps
    /// in the conversation — is read out of the stored copy's own headers and is an address by the time it is read, so
    /// the book is asked about what an author added and about nothing else.
    /// </para>
    /// </remarks>
    public IReadOnlyList<NamedRecipient> Recipients { get; init; } = [];
}

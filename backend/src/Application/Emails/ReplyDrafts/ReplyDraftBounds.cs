// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.ReplyDrafts;

/// <summary>How much of a correspondence one drafting reads, and how much of the person's own sent mail travels with it.</summary>
/// <param name="MaximumMessages">How many of the conversation's most recent messages the draft is grounded in.</param>
/// <param name="MaximumCharactersPerMessage">How much of one of those messages travels.</param>
/// <param name="MaximumStyleMessages">How many of the person's own recent sent messages the style is derived from, which is zero where a deployment turned that off.</param>
/// <param name="MaximumStyleCharactersPerMessage">How much of one sent message travels for that derivation.</param>
/// <remarks>
/// <para>
/// A value passed to the read rather than a constant inside it, so what one drafting sends to a provider is decided by
/// the use case that is answerable for the spend, and so a test can state a small correspondence without composing a
/// long one.
/// </para>
/// <para>
/// The style half is bounded separately and can be nothing at all. It is the one part of a drafting that reads mail
/// outside the conversation being answered, so the number of messages it reads is the number an operator is told
/// about, and zero is the whole of what turning the derivation off means here.
/// </para>
/// </remarks>
public sealed record ReplyDraftBounds(
    int MaximumMessages,
    int MaximumCharactersPerMessage,
    int MaximumStyleMessages,
    int MaximumStyleCharactersPerMessage);

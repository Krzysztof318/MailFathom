// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Emails.ReplyDrafts;

/// <summary>One drafting as the writer is given it: the correspondence, the people in it, the manner, and the ask.</summary>
/// <param name="Sources">What the draft is written from and cites, which carries no message where the draft answers nothing.</param>
/// <param name="Selection">The part of the correspondence the reply is to answer, or <see langword="null" /> to answer it as a whole.</param>
/// <param name="Instruction">What the person asked the reply to say, or <see langword="null" /> where they asked for nothing in particular.</param>
/// <param name="Language">The language this deployment writes for the person the draft is for, which decides a draft answering no correspondence.</param>
/// <remarks>
/// <para>
/// The two texts arrive here bounded by the use case rather than by the writer, because they are the one part of a
/// drafting somebody typed: what a person may put in front of a provider is a decision about this deployment's spend
/// and its data, and it belongs where the rest of that decision is taken.
/// </para>
/// <para>
/// The language is the person's rather than the mail's, and it decides only the case the mail cannot: a reply is
/// written in the language of the conversation it answers, because that is what the person on the other end reads, and
/// a message answering nothing has no such language to take. It is an argument here rather than a property of the
/// sources for the reason <see cref="Access.IMailUserLanguages" /> gives — whom a derivation is for stays out of the
/// text sent to a provider.
/// </para>
/// </remarks>
public sealed record ReplyDraftBrief(
    ReplyDraftSources Sources,
    string? Selection,
    string? Instruction,
    MailUserLanguage Language);

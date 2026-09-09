// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.ReplyDrafts;

/// <summary>One drafting as the writer is given it: the correspondence, the people in it, the manner, and the ask.</summary>
/// <param name="Sources">What the draft is written from and cites.</param>
/// <param name="Selection">The part of the correspondence the reply is to answer, or <see langword="null" /> to answer it as a whole.</param>
/// <param name="Instruction">What the person asked the reply to say, or <see langword="null" /> where they asked for nothing in particular.</param>
/// <remarks>
/// The two texts arrive here bounded by the use case rather than by the writer, because they are the one part of a
/// drafting somebody typed: what a person may put in front of a provider is a decision about this deployment's spend
/// and its data, and it belongs where the rest of that decision is taken.
/// </remarks>
public sealed record ReplyDraftBrief(ReplyDraftSources Sources, string? Selection, string? Instruction);

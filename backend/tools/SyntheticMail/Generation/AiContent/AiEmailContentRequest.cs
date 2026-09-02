// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Generation.AiContent;

/// <summary>One message's content, in the terms the generation is asked for.</summary>
/// <param name="LanguageCode">The code of the language the message is written in, as the invocation named it.</param>
/// <param name="Topic">The subject matter the message is written about.</param>
/// <param name="AuthorName">The display name the message is written as, drawn from the corpus's participant pool.</param>
/// <param name="ParentSubject">The subject of the message this one answers, or <see langword="null" /> when it opens a thread.</param>
/// <remarks>
/// What the deterministic generator decides arrives here, and only that: the envelope — who writes, to which thread,
/// in which language, about what — is seed-derived and reproducible, while the words are the source's to invent.
/// The author is named because a message that signs itself with a name different from the one the <c>From</c> header
/// carries is one a reader files as a defect rather than as mail.
/// </remarks>
internal sealed record AiEmailContentRequest(
    string LanguageCode,
    SyntheticMailTopic Topic,
    string AuthorName,
    string? ParentSubject);

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Generation.AiContent;

/// <summary>One message's content, in the terms the generation is asked for.</summary>
/// <param name="LanguageCode">The code of the language the message is written in, as the invocation named it.</param>
/// <param name="Topic">The subject matter the message is written about.</param>
/// <param name="MarkupDialect">The client whose markup the message's HTML form is written as.</param>
/// <param name="AuthorName">The display name the message is written as, drawn from the corpus's participant pool.</param>
/// <param name="ParentSubject">The subject of the message this one answers, or <see langword="null" /> when it opens a thread.</param>
/// <param name="ParentOpening">The opening of the message this one answers, bounded in length, or <see langword="null" /> when it opens a thread.</param>
/// <param name="AttachmentFileName">The name of the text file the message carries, or <see langword="null" /> when it carries none or carries an opaque one.</param>
/// <param name="AttachmentCharacterBound">The longest that file may be, and zero when none was asked for.</param>
/// <remarks>
/// <para>
/// What the deterministic generator decides arrives here, and only that: the envelope — who writes, to which thread,
/// in which language, about what, and in whose markup — is seed-derived and reproducible, while the words are the
/// source's to invent.
/// The author is named because a message that signs itself with a name different from the one the <c>From</c> header
/// carries is one a reader files as a defect rather than as mail.
/// </para>
/// <para>
/// The parent's opening is the one part of another message that travels with a request, and it is not an exception to
/// what this tool keeps off the wire: every word of it was written by the same run, either by a model answering an
/// earlier request or by the vocabulary in this repository, so nothing here has ever been near a real mailbox. What it
/// buys is a reply that answers something — a request carrying a subject alone produces a second message about the
/// same topic, which reads as a thread only in the headers.
/// </para>
/// <para>
/// The dialect is on the envelope's side of that line for the reason everything else here is: which client a message
/// looks like it came from is what a corpus varies deliberately, and a source left to choose would write the markup
/// it writes best. It reaches the prompt as the list of constructs the dialect names rather than as its name, because
/// a name is a thing to recognise and the list is a thing to write.
/// </para>
/// <para>
/// The last two move together and are the file the message carries. A corpus exists to be searched, and an attachment
/// filled with drawn bytes has nothing in it to find: the name says <c>tide-table.csv</c> and the part says nothing,
/// so the extractor is exercised and the search behind it is not. Naming the file to the same request that writes the
/// message is what makes the two say the same thing. Only a text attachment is asked for — an opaque one is opaque on
/// purpose and stays the drawn bytes it always was — and the bound is the size the seed drew for it, so how large the
/// files in a corpus are is still the seed's decision.
/// </para>
/// </remarks>
internal sealed record AiEmailContentRequest(
    string LanguageCode,
    SyntheticMailTopic Topic,
    SyntheticMarkupDialect MarkupDialect,
    string AuthorName,
    string? ParentSubject,
    string? ParentOpening,
    string? AttachmentFileName,
    int AttachmentCharacterBound);

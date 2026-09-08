// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Generation.AiContent;

/// <summary>What one generation answered: the subject line, the body as text, the body as the markup real mail carries, and the file the message attaches.</summary>
/// <param name="Subject">The subject line the message carries, or that the deterministic layer discards for a reply.</param>
/// <param name="Body">The body as plain text, with paragraphs separated by blank lines.</param>
/// <param name="Html">The same message as an HTML document, which is what the <c>text/html</c> alternative is written from.</param>
/// <param name="Attachment">The contents of the text file the message attaches, or <see langword="null" /> when the request asked for none.</param>
/// <remarks>
/// <para>
/// The two body forms are answered together rather than one being derived from the other, and that is the whole point
/// of this mode. The deterministic generator's HTML is one <c>&lt;p&gt;</c> per paragraph around its own text, so a
/// corpus built from it exercises MIME extraction and the client's document model against markup this repository
/// wrote. Real mail is what a sending client emitted — Word's own HTML, a campaign of nested layout tables, a web
/// composer's <c>&lt;div&gt;</c> soup, or markup that is not well-formed at all — and
/// <see cref="SyntheticMarkupDialect" /> is what the request names so the answer is one of those rather than the
/// well-formed document a model writes when nobody says otherwise.
/// </para>
/// <para>
/// Model-produced markup is untrusted by construction, which is what
/// <see cref="OpenAiEmailContentSource.ParseContent" /> checks before a message is built around it: an answer carrying
/// an executable construct — an element that runs, a <c>javascript:</c> URL, or an inline event handler — is refused
/// rather than reduced, because a corpus is delivered to a real mailbox and a development tool is not the place to
/// invent an attack. Everything the answer survives with is markup a reader has
/// to handle anyway.
/// </para>
/// <para>
/// The attachment is answered by the same call for the same reason the two body forms are: a corpus exists to be
/// searched, and a file whose name says <c>tide-table.csv</c> and whose part says nothing exercises the extractor
/// while leaving the search behind it with nothing to find. One call writes the message and the file it encloses, so
/// the two say the same thing. It is absent — <see langword="null" /> — for a message carrying no attachment and for
/// one carrying an opaque attachment, which is drawn bytes by design.
/// </para>
/// <para>
/// This is also the shape the model is asked to answer in — a JSON object with these keys — so the type is the
/// serialization contract as well as the result, and the source-generated context registers it once for both
/// directions of that agreement.
/// </para>
/// </remarks>
internal sealed record AiEmailContent(string Subject, string Body, string Html, string? Attachment = null);

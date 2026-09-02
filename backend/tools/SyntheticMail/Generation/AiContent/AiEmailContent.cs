// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Generation.AiContent;

/// <summary>What one generation answered: the subject line, the body as text, and the body as the markup real mail carries.</summary>
/// <param name="Subject">The subject line the message carries, or that the deterministic layer discards for a reply.</param>
/// <param name="Body">The body as plain text, with paragraphs separated by blank lines.</param>
/// <param name="Html">The same message as an HTML document, which is what the <c>text/html</c> alternative is written from.</param>
/// <remarks>
/// <para>
/// The two body forms are answered together rather than one being derived from the other, and that is the whole point
/// of this mode. The deterministic generator's HTML is one <c>&lt;p&gt;</c> per paragraph around its own text, so a
/// corpus built from it exercises MIME extraction and the client's document model against markup this repository
/// wrote. Real mail is headings, lists, tables, links, emphasis, and a signature block, and asking for that shape is
/// how the readers meet markup nobody here chose.
/// </para>
/// <para>
/// Model-produced markup is untrusted by construction, which is what
/// <see cref="OpenAiEmailContentSource.ParseContent" /> checks before a message is built around it: an answer carrying
/// an executable construct is refused rather than reduced, because a corpus is delivered to a real mailbox and a
/// development tool is not the place to invent an attack. Everything the answer survives with is markup a reader has
/// to handle anyway.
/// </para>
/// <para>
/// This is also the shape the model is asked to answer in — a JSON object with exactly these three keys — so the type
/// is the serialization contract as well as the result, and the source-generated context registers it once for both
/// directions of that agreement.
/// </para>
/// </remarks>
internal sealed record AiEmailContent(string Subject, string Body, string Html);

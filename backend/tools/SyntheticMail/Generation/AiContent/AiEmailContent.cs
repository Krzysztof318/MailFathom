// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Generation.AiContent;

/// <summary>What one generation answered: the subject line and the body, as plain text.</summary>
/// <param name="Subject">The subject line the message carries, or that the deterministic layer discards for a reply.</param>
/// <param name="Body">The body as plain text, with paragraphs separated by blank lines.</param>
/// <remarks>
/// <para>
/// The body arrives as plain text rather than HTML, for the reason the deterministic generator emits both alternatives
/// from one text: the HTML form is a wrapper the generator puts around the same paragraphs, and a source that answered
/// with markup would put model-produced tags into a corpus that exists to exercise the reader, not to trust them.
/// </para>
/// <para>
/// This is also the shape the model is asked to answer in — a JSON object with exactly these two keys — so the type is
/// the serialization contract as well as the result, and the source-generated context registers it once for both
/// directions of that agreement.
/// </para>
/// </remarks>
internal sealed record AiEmailContent(string Subject, string Body);

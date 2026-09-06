// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;

namespace MailFathom.Application.Emails.Enrichment;

/// <summary>One passage of a message a derivation reads, under the identity a mark cites it by.</summary>
/// <param name="Id">The passage as chunking wrote it down, which is what evidence names.</param>
/// <param name="Ordinal">Its position in the message, counted from zero in reading order.</param>
/// <param name="Text">The passage itself.</param>
/// <remarks>
/// <para>
/// A derivation reads passages rather than the message's text, and that is what makes its evidence checkable: the
/// producer is shown the same units a citation resolves to, so a mark can name where its claim came from instead of
/// gesturing at the message. It also means a derivation reads text every switched-on scanner has already redacted,
/// because that is the text chunking cut.
/// </para>
/// <para>
/// The ordinal is what a producer names a passage by. The identifier is never shown to one: a producer that answered
/// with an identifier could answer with any identifier, and a position in a list it was given cannot name a passage of
/// somebody else's mail.
/// </para>
/// </remarks>
public sealed record EnrichablePassage(EmailChunkId Id, int Ordinal, string Text);

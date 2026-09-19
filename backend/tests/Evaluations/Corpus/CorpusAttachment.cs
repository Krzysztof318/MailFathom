// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Evaluations.Corpus;

/// <summary>One file a corpus message attached, as the attachment index a deployment keeps would name it.</summary>
/// <param name="FileName">The name the sender wrote, or <see langword="null" /> where the part carried none.</param>
/// <param name="DeclaredMediaType">The type the sender declared.</param>
internal sealed record CorpusAttachment(string? FileName, string DeclaredMediaType);

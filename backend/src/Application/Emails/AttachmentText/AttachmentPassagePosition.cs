// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.AttachmentText;

/// <summary>Where a read of one message's attachment passages stopped, which is what the next page continues past.</summary>
/// <remarks>
/// The two columns the passages of one message are ordered by, in that order, and therefore the whole of the keyset: a
/// message holds at most one passage per attachment position and ordinal, so the pair is unique within the message and
/// no tie-breaker is needed. A caller builds one from the last <see cref="AttachmentPassage" /> it received rather than
/// counting rows, so a passage written or erased between two pages moves nothing that has already been read.
/// </remarks>
/// <param name="AttachmentPosition">The walk position of the attachment the last passage read belongs to.</param>
/// <param name="Ordinal">That passage's place in the attachment's own text.</param>
public readonly record struct AttachmentPassagePosition(int AttachmentPosition, int Ordinal);

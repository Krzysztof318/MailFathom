// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Screening;
using MailFathom.Domain.Delivery.Drafts;

namespace MailFathom.Application.Mail.Delivery.Drafts;

/// <summary>One draft opened for editing: the record it is listed by, and the words its stored message carries.</summary>
/// <remarks>
/// The two halves are read from two places on purpose. The record is the row, which a listing already answered with and
/// which says who the draft is addressed to and what state its server copy is in; the text is parsed out of the stored
/// message, so what an author is given back to edit is what would actually be sent rather than a second copy of it.
/// </remarks>
/// <param name="Draft">The record, which is what a listing already answered with.</param>
/// <param name="Text">The subject and the two body representations, read back out of the composed message.</param>
public sealed record MailDraftReading(MailDraftRecord Draft, OutgoingMailText Text);

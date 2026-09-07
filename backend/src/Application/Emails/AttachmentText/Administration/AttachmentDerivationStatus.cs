// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText.Limits;

namespace MailFathom.Application.Emails.AttachmentText.Administration;

/// <summary>Where reading a deployment's attachments and images stands: how far it has come, and what it has consumed.</summary>
/// <param name="Coverage">How much has been read, how much waits, and what was skipped with its reason.</param>
/// <param name="Extraction">What this period has read out of attachments, and what it still admits.</param>
/// <param name="Description">What this period has asked a chat provider to describe, and what it still admits.</param>
/// <remarks>
/// The three are one value because an operator acting on any of them needs the other two: a mailbox that has stopped
/// advancing is either finished, waiting on a ceiling, or waiting on a run, and only the coverage beside both periods
/// says which. Reported apart from the embedding figures beside it, because the two are paid for in different units and
/// a mailbox may be entirely embedded on its message text while every attachment in it is still unread.
/// </remarks>
public sealed record AttachmentDerivationStatus(
    AttachmentDerivationCoverage Coverage,
    AttachmentDerivationPeriod Extraction,
    AttachmentDerivationPeriod Description);

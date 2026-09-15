// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.AttachmentText;

/// <summary>One stored message whose attachments nothing has read yet, as the selection returns it.</summary>
/// <remarks>
/// The account travels with the message because redaction is resolved from the mailbox rather than the deployment:
/// one copy is read once however many users are assigned it, so the posture is the mailbox's own. The count the
/// classification recorded does not travel: the walk over the stored message publishes its own, and the two disagree
/// whenever the row was written by an older reading, so the reading asks the walk rather than the row.
/// </remarks>
/// <param name="Id">The message whose attachments are read.</param>
/// <param name="Account">The mailbox it is in, which decides what a redaction looks for and whose ceilings bound it.</param>
/// <param name="Admission">Which of the classification gate's answers let this message through.</param>
public sealed record EmailAwaitingAttachmentText(
    StoredEmailId Id,
    MailAccountId Account,
    DerivedWorkAdmission Admission);

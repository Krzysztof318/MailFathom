// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.AttachmentText;

/// <summary>One stored message whose attachments nothing has read yet, as the selection returns it.</summary>
/// <remarks>
/// The account travels with the message because redaction is the account's rather than the deployment's, and the user
/// beside it because what a derivation spends is charged to the person. The count the classification recorded travels
/// with neither: the walk over the stored message publishes its own, and the two disagree whenever the row was written
/// by an older reading, so the reading asks the walk rather than the row.
/// </remarks>
/// <param name="Id">The message whose attachments are read.</param>
/// <param name="User">Whose mail it is, which is what a spend is charged against.</param>
/// <param name="Account">Which mailbox it is in, which decides what a redaction looks for.</param>
/// <param name="Admission">Which of the classification gate's answers let this message through.</param>
public sealed record EmailAwaitingAttachmentText(
    StoredEmailId Id,
    MailUserId User,
    MailAccountId Account,
    DerivedWorkAdmission Admission);

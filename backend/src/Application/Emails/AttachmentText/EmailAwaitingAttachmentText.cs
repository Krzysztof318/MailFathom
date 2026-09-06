// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.AttachmentText;

/// <summary>One stored message whose attachments nothing has read yet, as the selection returns it.</summary>
/// <remarks>
/// The owner travels with the message because redaction is the owner's rather than the deployment's, and the count
/// travels with it because the walk that opens the attachments needs to know how many positions to ask for — the
/// per-attachment list of names, types, and sizes is deliberately not persisted, so the count is the whole of what the
/// row can answer about them.
/// </remarks>
/// <param name="Id">The message whose attachments are read.</param>
/// <param name="Owner">Whose mail it is, which decides what a redaction looks for.</param>
/// <param name="AttachmentCount">How many parts the classification counted as attachments.</param>
/// <param name="Admission">Which of the classification gate's answers let this message through.</param>
public sealed record EmailAwaitingAttachmentText(
    StoredEmailId Id,
    MailOwnerId Owner,
    int AttachmentCount,
    DerivedWorkAdmission Admission);

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Mailboxes;

/// <summary>Says whether a mailbox read reaches into the account's junk folder.</summary>
/// <remarks>
/// <para>
/// Excluding it is the default because of what the folder holds rather than because of what MailFathom concluded: mail
/// in it is there because the provider or the owner put it there, and it is disproportionately content written to
/// deceive whoever reads it — which now includes an agent answering questions about the mailbox. It is true of the
/// mailbox with no scanner deployed and with nothing ever classified, which is why the exclusion is a property of a
/// mailbox read rather than of the classification feature.
/// </para>
/// <para>
/// The override is the caller's and is deliberately explicit. A caller looking for a message the provider filed wrongly
/// has to be able to find it, and a result that quietly omitted a whole folder would leave them concluding the message
/// is gone.
/// </para>
/// </remarks>
public enum JunkMailInclusion
{
    /// <summary>Return nothing from the account's junk folder, which is what a caller that asked for nothing gets.</summary>
    Excluded = 0,

    /// <summary>Return the junk folder's mail alongside everything else, because the caller asked for it.</summary>
    Included = 1,
}

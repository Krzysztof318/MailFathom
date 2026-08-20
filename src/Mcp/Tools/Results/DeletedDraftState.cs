// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Publishes what giving up a draft did to the copy of it in the owner's mailbox.</summary>
/// <remarks>
/// The draft is given up in every one of these — MailFathom holds nothing for it afterwards or is on its way to holding
/// nothing — and what they differ in is whether the owner will still see a message in their drafts folder. That is the
/// one thing a caller has to be able to tell somebody, and it is not something the removal can promise: a mail server
/// may refuse to give a copy up, and the folder a copy was appended to may no longer be the folder the account means
/// by drafts.
/// </remarks>
internal enum DeletedDraftState
{
    /// <summary>The draft is gone and no copy of it is left in the mailbox.</summary>
    [Description("The draft is gone and nothing is left in the owner's drafts folder for it.")]
    Deleted = 0,

    /// <summary>The draft is gone and a copy the mail server would not give up stands in the mailbox.</summary>
    [Description("The draft is gone here, and one copy of it could not be taken out of the mailbox — the mail server refused it, or the folder it was put in is no longer the one this account means by drafts. The owner may still see that message and can delete it in their own mail client; nothing here will touch it again.")]
    CopyLeftBehind = 1,

    /// <summary>The draft is marked as given up and the mailbox has not been settled yet.</summary>
    [Description("The draft is marked as given up and this deployment holds nothing further for it, but the mailbox could not be reached to take the copy out. A later pass finishes that, so the owner may see the message in their drafts folder until it does.")]
    Pending = 2,
}

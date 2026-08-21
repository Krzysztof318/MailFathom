// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;

namespace MailFathom.Mcp.Tools.Drafts;

/// <summary>Publishes whether the owner's own drafts folder shows the draft as it now stands.</summary>
/// <remarks>
/// The two facts a caller has are not one fact. MailFathom holds the draft the moment the call answers — it can be
/// edited, deleted, and sent from that instant — while the copy in the mailbox is appended over a network round trip
/// afterwards, and an account that maps no drafts folder never gets one at all. Saying which of the two has happened
/// is what stops a caller from telling somebody to look in a folder that does not show the message yet.
/// </remarks>
internal enum SavedDraftState
{
    /// <summary>MailFathom holds the draft and the mailbox does not yet show this version of it.</summary>
    [Description("MailFathom holds the draft and the owner's drafts folder does not show this version of it yet. The draft can be updated, deleted, and sent from now; the copy is appended by the next pass over the mailbox, and an account that maps no drafts folder keeps its drafts here and shows none of them.")]
    Held = 0,

    /// <summary>The copy in the owner's drafts folder is this version of the draft.</summary>
    [Description("The owner's drafts folder shows this version of the draft, so it can be read in their own mail client.")]
    Filed = 1,
}

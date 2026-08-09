// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Says what an operator asked to start an account's next synchronization pass, as the protocol spells it.</summary>
/// <remarks>
/// The transport carries its own enumeration rather than the domain's, so the published wire values are decided here and
/// a rename inside the domain stays a rename. The member names are the wire values — they are serialized camel-cased —
/// which is exactly why they cannot be shared with a type whose names exist to describe the domain.
/// </remarks>
internal enum AccountSynchronizationMode
{
    /// <summary>The account's folders are reconciled on its configured interval and nothing else starts a pass.</summary>
    Polling = 0,

    /// <summary>The account holds a session that waits for the mail server to report a change, and a change starts a pass at once.</summary>
    Push = 1,
}

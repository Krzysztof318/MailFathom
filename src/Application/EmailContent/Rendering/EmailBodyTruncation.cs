// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering;

/// <summary>Names which bound cut a body representation short, or states that none did.</summary>
/// <remarks>
/// A caller that reads several emails in one call has two limits between it and a whole message, and they mean different
/// things: one is a property of the message it asked about, the other a property of how much it asked for at once. A
/// single flag would report both as "incomplete" and leave the caller with no way to tell a message worth reading alone
/// from a batch worth splitting.
/// </remarks>
public enum EmailBodyTruncation
{
    /// <summary>The representation is the whole of what the message displayed.</summary>
    None = 0,

    /// <summary>The per-representation bound cut it, so this message alone is longer than one read returns.</summary>
    /// <remarks>Reading the same message again returns exactly the same prefix; what is missing is beyond what any single call publishes.</remarks>
    BodyCharacterLimit = 1,

    /// <summary>The read's total character budget cut it, because earlier emails in the same call had already spent it.</summary>
    /// <remarks>Naming this email in a call of its own returns more of it, which is the action the state exists to make visible.</remarks>
    ReadCharacterBudget = 2,
}

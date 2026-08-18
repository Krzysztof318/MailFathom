// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails;

/// <summary>States what a message said about being sent by a machine rather than written to one person.</summary>
/// <remarks>
/// <para>
/// Each value but the first is a claim the sender itself made in a header defined for exactly that purpose, so this is
/// read rather than guessed: a mailing list stamps <c>List-Id</c> (RFC 2919) or the <c>List-*</c> headers RFC 2369
/// defines, an automatic responder stamps <c>Auto-Submitted</c> (RFC 3834), and bulk mail has stamped
/// <c>Precedence</c> since long before either. A message carrying none of them says nothing, which is
/// <see cref="None" /> rather than a statement that a person wrote it.
/// </para>
/// <para>
/// Which of the three a message carried is kept rather than flattened into a flag, because they are different claims
/// and a later reader may want them apart: a list posting is written by a person to many, while an automatic reply is
/// written by nobody at all.
/// </para>
/// </remarks>
public enum EmailAutomation
{
    /// <summary>The message claimed nothing, which every ordinary message a person wrote also does.</summary>
    None = 0,

    /// <summary>The message was distributed by a mailing list, which stamped its own identity onto it.</summary>
    MailingList = 1,

    /// <summary>The message was submitted automatically rather than by a person composing it.</summary>
    AutomaticallySubmitted = 2,

    /// <summary>The message declared itself bulk, list, or junk precedence.</summary>
    BulkPrecedence = 3,
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>Whether this deployment counts a message's authenticated author as somebody it knows.</summary>
/// <remarks>
/// <para>
/// This is a decision about a list an operator and a reader write to, and it is deliberately independent of what the
/// receiving server established: <see cref="SenderAuthentication" /> answers whether an identity held, and this answers
/// whether the identity that held is one this deployment recognizes. Reading the two together is what makes a message
/// legible — an authenticated author nobody has named reads very differently from an author whose authentication failed,
/// and both are <see cref="Unknown" /> here.
/// </para>
/// <para>
/// <see cref="Unknown" /> is therefore the ordinary answer rather than a negative one. Most legitimate mail arrives from
/// a correspondent nobody has ever named and reads exactly as a forgery does at this level, so nothing derived from this
/// value alone may read as an accusation. Whether a message is unwanted is spam classification's question and is reached
/// by other means entirely.
/// </para>
/// </remarks>
public enum SenderTrustLevel
{
    /// <summary>This deployment recognizes nobody in the message, whether or not an author was authenticated.</summary>
    Unknown = 0,

    /// <summary>The authenticated author is one this deployment recognizes, and the verdict says what named them.</summary>
    Trusted = 1,
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Emails;

/// <summary>The failure raised when a continuation cursor is not one this system issued.</summary>
/// <remarks>
/// <para>
/// A cursor is opaque to its holder, so the only cursor a caller can legitimately present is one a previous page
/// returned. Anything else — a truncated string, a value from a different build, a hand-written one — is refused rather
/// than interpreted as far as it parses, because a partially understood cursor would name a page boundary nobody
/// computed and would silently skip or repeat rows.
/// </para>
/// <para>
/// The message never repeats the rejected text. It is caller-supplied input that reaches operator-facing output, and
/// echoing it would put an unbounded, unvalidated string into a log for no diagnostic gain.
/// </para>
/// </remarks>
public sealed class MailboxQueryCursorMalformedException : MailFathomException
{
    /// <summary>Initializes the failure for one unreadable cursor.</summary>
    public MailboxQueryCursorMalformedException()
        : base("The continuation cursor is not one this mailbox query issued.")
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.MailboxQueryCursorMalformed;
}

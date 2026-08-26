// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Emails.BrowseThread;

/// <summary>The failure raised when a conversation's continuation cursor names a message that conversation no longer shows.</summary>
/// <remarks>
/// A thread's order is derived on every read, so a page boundary is the message the previous page ended on rather than a
/// position in a list. A message deleted since, or moved into a folder the caller may no longer read, leaves the walk
/// nothing to continue after — and answering with the conversation's first page instead would read on screen as the
/// thread having jumped back to the top.
/// </remarks>
public sealed class EmailThreadCursorMessageMissingException : MailFathomException
{
    /// <summary>Initializes the failure for one cursor whose message is no longer in the conversation.</summary>
    public EmailThreadCursorMessageMissingException()
        : base("The continuation cursor names a message this conversation no longer shows, so the walk cannot be continued after it.")
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.EmailThreadCursorMessageMissing;
}

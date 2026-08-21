// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Threads;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Emails.Threads;

/// <summary>Reads one persistence row as the four values assembly decides a conversation from.</summary>
/// <remarks>
/// Written once because three callers need it and they must agree: the store answering assembly's questions, and both
/// write paths handing it the message they are committing. A second copy that forgot the referenced ancestors would
/// leave one path assembling conversations the other could not.
/// </remarks>
internal static class ThreadedEmails
{
    /// <summary>Reads the assembly view of one stored email.</summary>
    /// <param name="email">The row to read.</param>
    /// <returns>The identity, the identifiers, and the reply relation the row records.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="email" /> is <see langword="null" />.</exception>
    public static ThreadedEmail Of(StoredEmailEntity email)
    {
        ArgumentNullException.ThrowIfNull(email);

        return new ThreadedEmail
        {
            StoredEmailId = StoredEmailId.Create(email.Id),
            InternetMessageId = email.InternetMessageId,
            AnsweredInternetMessageId = email.InReplyTo,
            ReferencedInternetMessageIds = email.ThreadReferences,
            AnsweredStoredEmailId = email.ParentStoredEmailId is { } answered
                ? StoredEmailId.Create(answered)
                : null,
        };
    }
}

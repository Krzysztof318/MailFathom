// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Threads;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Answers thread reads from a list held in memory, keyed by the thread each message belongs to.</summary>
/// <remarks>
/// A hand-written fake rather than a substitute, because what these tests arrange is a conversation rather than a call:
/// the ordering, the visibility filtering, and the bound are all decided from what comes back, so the double's job is to
/// hold a set of messages and hand back the ones a thread names.
/// </remarks>
internal sealed class StubEmailThreadReader(
    params IReadOnlyList<(EmailThreadId ThreadId, ThreadedEmailSummary Message)> messages)
    : IEmailThreadReader
{
    /// <summary>Gets how many times a thread was read, which is what proves one read assembles a conversation once.</summary>
    public int ReadCount { get; private set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<ThreadedEmailSummary>> ReadEmailsAsync(
        EmailThreadId threadId,
        CancellationToken cancellationToken)
    {
        this.ReadCount++;

        return Task.FromResult<IReadOnlyList<ThreadedEmailSummary>>(
        [
            .. messages
                .Where(held => held.ThreadId == threadId)
                .Select(held => held.Message),
        ]);
    }
}

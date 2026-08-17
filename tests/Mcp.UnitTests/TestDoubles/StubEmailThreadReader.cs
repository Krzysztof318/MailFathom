// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Threads;
using MailFathom.Domain.Emails;

namespace MailFathom.Mcp.UnitTests.TestDoubles;

/// <summary>Answers thread reads from a list held in memory, keyed by the thread each message belongs to.</summary>
internal sealed class StubEmailThreadReader(params IReadOnlyList<(EmailThreadId ThreadId, ThreadedEmailSummary Email)> emails)
    : IEmailThreadReader
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ThreadedEmailSummary>> ReadEmailsAsync(
        EmailThreadId threadId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ThreadedEmailSummary>>(
        [
            .. emails
                .Where(held => held.ThreadId == threadId)
                .Select(held => held.Email),
        ]);
}

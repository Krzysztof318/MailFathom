// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Delivery;

/// <summary>Composes the queries a send ceiling is counted from.</summary>
/// <remarks>
/// <para>
/// A type of its own so what the counts mean can be read as SQL rather than inferred from a reader that awaits them.
/// Both halves are easy to get wrong in a way only the database would notice: a window written as a range over the
/// instant a record was written is what the epoch-anchored period means, and the recipients are counted over their own
/// rows rather than summed per record.
/// </para>
/// <para>
/// Nothing here filters by stage. What a ceiling counts is every message a period was asked for, whatever became of it,
/// because a send that a server refused was still mail this deployment tried to put on the network — and a count that
/// forgot those would let a period which failed entirely be spent twice.
/// </para>
/// </remarks>
internal static class OutgoingMailUsageQuery
{
    /// <summary>Composes the query over the messages a period was asked for.</summary>
    /// <param name="messages">The outgoing records to ask, ordinarily read without tracking.</param>
    /// <param name="periodStart">The instant the period began, as the ceilings place it.</param>
    /// <param name="accountId">The account to narrow to, or <see langword="null" /> for every account of the deployment.</param>
    /// <returns>The query whose count is the messages the period holds.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages" /> is <see langword="null" />.</exception>
    internal static IQueryable<OutgoingEmailEntity> ComposeMessages(
        IQueryable<OutgoingEmailEntity> messages,
        DateTimeOffset periodStart,
        MailAccountId? accountId)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var withinPeriod = messages.Where(message => message.RecordedAt >= periodStart);

        return accountId is { } account
            ? withinPeriod.Where(message => message.MailboxAccountId == account.Value)
            : withinPeriod;
    }

    /// <summary>Composes the query over the people those messages are addressed to.</summary>
    /// <param name="messages">The messages whose recipients are counted, as <see cref="ComposeMessages" /> narrowed them.</param>
    /// <returns>The query whose count is the recipients the period holds.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Flattened onto the recipient rows so the answer is one count over a join, rather than a correlated subquery per
    /// record that a period holding thousands would pay for once each. No column of a recipient is read, which is what
    /// keeps an address out of a query that runs on every send.
    /// </remarks>
    internal static IQueryable<OutgoingEmailRecipientEntity> ComposeRecipients(
        IQueryable<OutgoingEmailEntity> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return messages.SelectMany(message => message.Recipients);
    }
}

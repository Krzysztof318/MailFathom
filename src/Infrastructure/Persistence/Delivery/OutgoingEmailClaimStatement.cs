// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Domain.Delivery;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Delivery;

/// <summary>Composes the one statement a claim over the outbox is.</summary>
/// <remarks>
/// <para>
/// Written rather than composed through the query provider, because the claim is the mechanism rather than a query:
/// <c>FOR UPDATE SKIP LOCKED</c> is what makes two workers claiming at the same moment take different sends instead of
/// waiting on each other, and no LINQ operator expresses it. Selecting and stamping in one statement is what makes the
/// claim atomic — a read followed by a write would leave the window in which both workers saw the same row, and the
/// thing a duplicated claim produces here is a second copy in somebody's mailbox.
/// </para>
/// <para>
/// It is a type of its own so the statement can be read and asserted without a database. Losing the stage filter, the
/// locking clause, or the bound would each fail silently at run time — as a message sent twice, as duplicated work, or
/// as a claim that drains the queue — so the statement is verified as text.
/// </para>
/// <para>
/// The predicate names exactly one stage, and that is the safety property rather than an optimization. A record at
/// <see cref="OutgoingEmailStage.TransmissionBegun" /> has had its body offered to a server that never answered, and
/// nothing an outbox can read afterwards says whether it landed; taking it here because its lease expired is precisely
/// the mistake an expiry-based recovery makes if nobody stops it. What an expiry does reach is a record that had issued
/// no SMTP command at all, which is safe to attempt again because it reached nobody.
/// </para>
/// <para>
/// Every value is a parameter. The identifiers are quoted because EF Core names the columns after the properties, which
/// PostgreSQL would otherwise fold to lower case and fail to find.
/// </para>
/// </remarks>
internal static class OutgoingEmailClaimStatement
{
    /// <summary>Composes the statement that takes and stamps a batch of the account's due sends.</summary>
    /// <param name="request">Whose sends to take, how many, and under what lease.</param>
    /// <param name="claimedAt">The instant the claim is judged and stamped at.</param>
    /// <returns>The statement, whose rows are the identifiers of the sends this claim took.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    internal static FormattableString Compose(OutgoingEmailClaimRequest request, DateTimeOffset claimedAt)
    {
        ArgumentNullException.ThrowIfNull(request);

        var accountId = request.AccountId.Value;
        var recorded = nameof(OutgoingEmailStage.Recorded);
        var leaseOwner = request.Owner;
        var leaseExpiresAt = claimedAt + request.LeaseDuration;
        var batchSize = request.BatchSize;

        // Two predicates make a send due, and the second is the crash recovery: one nothing holds whose next-attempt
        // instant has passed, and one whose lease has run out. The locking clause follows LIMIT, which is where the
        // standard puts it and where the limit counts the rows that survived locking, so a batch of one against a row
        // another worker holds takes the next free row rather than coming back empty.
        return $"""
                WITH due AS (
                    SELECT candidate."Id"
                    FROM outgoing_emails AS candidate
                    WHERE candidate."{nameof(OutgoingEmailEntity.Stage)}" = {recorded}
                      AND candidate."{nameof(OutgoingEmailEntity.MailboxAccountId)}" = {accountId}
                      AND candidate."{nameof(OutgoingEmailEntity.AvailableAt)}" <= {claimedAt}
                      AND (candidate."{nameof(OutgoingEmailEntity.LeaseExpiresAt)}" IS NULL
                        OR candidate."{nameof(OutgoingEmailEntity.LeaseExpiresAt)}" <= {claimedAt})
                    ORDER BY candidate."{nameof(OutgoingEmailEntity.AvailableAt)}", candidate."Id"
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED
                )
                UPDATE outgoing_emails AS outgoing
                SET "{nameof(OutgoingEmailEntity.LeaseOwner)}" = {leaseOwner},
                    "{nameof(OutgoingEmailEntity.LeaseExpiresAt)}" = {leaseExpiresAt},
                    "{nameof(OutgoingEmailEntity.AttemptCount)}" = outgoing."{nameof(OutgoingEmailEntity.AttemptCount)}" + 1
                FROM due
                WHERE outgoing."Id" = due."Id"
                RETURNING outgoing."Id" AS "Value"
                """;
    }
}

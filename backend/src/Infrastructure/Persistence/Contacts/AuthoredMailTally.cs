// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts.Collection;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Contacts;

/// <summary>Counts an account's stored mail by the address that wrote it, out of PostgreSQL.</summary>
/// <remarks>
/// <para>
/// Answered from the index over the sender's comparison form, which the stored mail already carries because the mailbox
/// timeline filters on it. That is what lets collection have a threshold without a ledger of its own: the count is read
/// from mail that is already held rather than accumulated beside it.
/// </para>
/// <para>
/// Both halves stop at the ceiling the caller asked for, so the cost of the question is the threshold rather than the
/// number of messages one prolific correspondent wrote. The split is what makes a message stored in two folders of one
/// account count once: the copies of one message share the identifier its sender wrote, so the identified half counts
/// distinct identifiers, while a message whose sender wrote none has nothing to be recognized by and is counted per
/// stored copy.
/// </para>
/// <para>
/// Nothing logs. The address is personal data and so is the count of somebody's mail, which is why the answer goes back
/// to the caller rather than to an instrument.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class AuthoredMailTally(MailFathomDbContext readContext) : IAuthoredMailTally
{
    /// <inheritdoc />
    public async Task<int> CountMessagesAuthoredByAsync(
        MailAccountIdentity account,
        EmailAddress author,
        int ceiling,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ceiling);

        var ownerValue = account.Owner.Value;
        var accountValue = account.Id.Value;
        var normalizedAddress = author.NormalizedAddress;

        var authored = readContext.StoredEmails
            .AsNoTracking()
            .Where(email =>
                email.OwnerId == ownerValue
                && email.MailboxAccountId == accountValue
                && email.SenderNormalizedAddress == normalizedAddress);

        var identified = await authored
            .Where(email => email.InternetMessageId != null)
            .Select(email => email.InternetMessageId)
            .Distinct()
            .Take(ceiling)
            .CountAsync(cancellationToken);

        if (identified >= ceiling)
        {
            return ceiling;
        }

        var unidentified = await authored
            .Where(email => email.InternetMessageId == null)
            .Take(ceiling - identified)
            .CountAsync(cancellationToken);

        return identified + unidentified;
    }
}

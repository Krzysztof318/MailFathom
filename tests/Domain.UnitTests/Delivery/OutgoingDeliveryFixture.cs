// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;

namespace MailFathom.Domain.UnitTests.Delivery;

/// <summary>Builds the outgoing values the delivery tests state their scenarios in.</summary>
/// <remarks>Every address here is synthetic and belongs to a reserved test domain.</remarks>
internal static class OutgoingDeliveryFixture
{
    /// <summary>Names one recipient, refusing an address the tests themselves wrote wrongly.</summary>
    internal static OutgoingRecipient Recipient(string address, OutgoingRecipientRole role)
    {
        if (!EmailAddress.TryCreate(displayName: null, address, out var emailAddress))
        {
            throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
        }

        return OutgoingRecipient.Create(emailAddress, role);
    }

    /// <summary>Builds a record at one stage, with the recipient outcomes the scenario is about.</summary>
    internal static OutgoingEmailRecord Record(
        OutgoingEmailStage stage,
        params OutgoingRecipientOutcome[] recipients) => new()
        {
            Id = OutgoingEmailId.Create(Guid.Parse("0198f0a0-1111-7000-8000-000000000001")),
            AccountId = MailAccountId.Create("work"),
            Requester = OutgoingEmailRequester.Command("mfctl-4f2a"),
            Principal = OutgoingEmailPrincipal.Of("operator"),
            Recipients = recipients,
            Stage = stage,
            MimeByteLength = 4096,
            AttemptCount = 1,
            RecordedAt = DateTimeOffset.Parse("2026-08-16T10:00:00Z", CultureInfo.InvariantCulture),
            StageChangedAt = DateTimeOffset.Parse("2026-08-16T10:00:05Z", CultureInfo.InvariantCulture),
            AvailableAt = DateTimeOffset.Parse("2026-08-16T10:00:00Z", CultureInfo.InvariantCulture),
            LastFailure = null,
            LastReplyCode = null,
            Filings = [],
            LastFilingFailure = null,
        };
}

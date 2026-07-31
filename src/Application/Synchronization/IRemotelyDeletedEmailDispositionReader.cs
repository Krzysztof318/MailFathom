// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;

namespace MailMcp.Application.Synchronization;

/// <summary>Answers what one account does locally with an email its mail server no longer holds.</summary>
/// <remarks>
/// The disposition is read per account rather than per deployment because the accounts of one deployment are not
/// interchangeable: a mailbox whose provider is the system of record can be followed exactly, while a mailbox MailMcp is
/// the durable copy of must not lose mail because the server dropped it. Reading it through a port keeps that decision
/// where the other per-account decisions are — configuration — instead of letting reconciliation reach for a settings
/// type of its own.
/// </remarks>
public interface IRemotelyDeletedEmailDispositionReader
{
    /// <summary>Gets the disposition configured for one account.</summary>
    /// <param name="accountId">The account being reconciled.</param>
    /// <returns>What becomes of the local copy of an email that account's server no longer holds.</returns>
    RemotelyDeletedEmailDisposition GetDisposition(MailAccountId accountId);
}

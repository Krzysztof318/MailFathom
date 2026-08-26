// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Mail.Delivery.Outbox;

/// <summary>States what one claim takes out of the outbox: whose sends, how many, and under what lease.</summary>
/// <remarks>
/// <para>
/// A claim names one account, because failure is isolated per account: a provider that is unreachable stalls the sends
/// of the mailbox it serves and no others, and a claim that spanned accounts would put them behind one another in a
/// single pass.
/// </para>
/// <para>
/// One claimant covers the whole batch, because the claim that stamped them is one statement. It identifies the pass
/// rather than the process, which is what lets a write be refused once the lease has moved on.
/// </para>
/// </remarks>
public sealed record OutgoingEmailClaimRequest
{
    private OutgoingEmailClaimRequest(
        MailAccountIdentity account,
        int batchSize,
        TimeSpan leaseDuration,
        Guid claimant)
    {
        this.Account = account;
        this.BatchSize = batchSize;
        this.LeaseDuration = leaseDuration;
        this.Claimant = claimant;
    }

    /// <summary>Gets the account whose queued sends this claim takes, named by its owner and its identifier.</summary>
    public MailAccountIdentity Account { get; }

    /// <summary>Gets the greatest number of records the claim takes.</summary>
    public int BatchSize { get; }

    /// <summary>Gets how long the claim holds each record it takes.</summary>
    public TimeSpan LeaseDuration { get; }

    /// <summary>Gets the attempt the claimed records are stamped for, which is what holds the lease.</summary>
    public Guid Claimant { get; }

    /// <summary>States a claim to make.</summary>
    /// <param name="account">The account whose queued sends the claim takes.</param>
    /// <param name="batchSize">The greatest number of records to take.</param>
    /// <param name="leaseDuration">How long the claim holds each record.</param>
    /// <returns>The claim to issue, stamped for a new attempt.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="batchSize" /> is not positive or <paramref name="leaseDuration" /> is not positive.</exception>
    /// <remarks>The claimant is generated here rather than supplied, so no caller can claim under an identity another attempt already holds.</remarks>
    public static OutgoingEmailClaimRequest Create(
        MailAccountIdentity account,
        int batchSize,
        TimeSpan leaseDuration)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);

        return new OutgoingEmailClaimRequest(account, batchSize, leaseDuration, Guid.CreateVersion7());
    }
}

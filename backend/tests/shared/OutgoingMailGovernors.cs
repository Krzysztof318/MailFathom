// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery.Governance;

namespace MailFathom.TestSupport;

/// <summary>Builds the bounds the outbox asks before it writes a send down, over a posture a test states.</summary>
/// <remarks>
/// Every path into the outbox now passes them, so a test arranging a send that is meant to succeed would otherwise
/// spell the same permissive posture out in each suite. What a test states here is what an operator would have
/// configured — sending on or off, who may be written to, and what a period admits — rather than a substitute of the
/// governor itself, so a suite proving something about a send is held to the same decision the deployment makes.
/// </remarks>
internal static class OutgoingMailGovernors
{
    /// <summary>Builds the bounds of a deployment that has turned sending on and configured nothing else.</summary>
    /// <returns>The governor a send passes.</returns>
    internal static OutgoingMailGovernor Permitting() => Governing();

    /// <summary>Builds the bounds a test states, each part defaulting to the posture that refuses nothing.</summary>
    /// <param name="refusal">What withholds the capability to send, or <see langword="null" /> when sending is on.</param>
    /// <param name="recipientPolicy">Who this deployment may write to, or <see langword="null" /> for anybody.</param>
    /// <param name="ceilings">What one period admits, or <see langword="null" /> for no ceiling at all.</param>
    /// <param name="usage">What the period has already been asked for.</param>
    /// <param name="timeProvider">The clock the period is placed by, or <see langword="null" /> for the system clock.</param>
    /// <returns>The governor the outbox asks.</returns>
    internal static OutgoingMailGovernor Governing(
        OutgoingSendRefusalReason? refusal = null,
        OutgoingRecipientPolicy? recipientPolicy = null,
        OutgoingMailCeilings? ceilings = null,
        OutgoingMailUsage usage = default,
        TimeProvider? timeProvider = null) =>
        new(
            new StatedSendPermissions(refusal),
            recipientPolicy ?? OutgoingRecipientPolicy.Unrestricted,
            ceilings ?? OutgoingMailCeilings.Unbounded,
            new StatedUsage(usage),
            timeProvider ?? TimeProvider.System);

    /// <summary>Reports the one posture a test stated, for every account it asks about.</summary>
    private sealed class StatedSendPermissions(OutgoingSendRefusalReason? refusal) : IOutgoingSendPermissionReader
    {
        public OutgoingSendRefusalReason? FindRefusal(MailAccountId accountId) => refusal;
    }

    /// <summary>Reports the one period reading a test stated, whichever period is asked about.</summary>
    private sealed class StatedUsage(OutgoingMailUsage usage) : IOutgoingMailUsageReader
    {
        public Task<OutgoingMailUsage> ReadUsageSinceAsync(
            MailAccountIdentity account,
            DateTimeOffset periodStart,
            CancellationToken cancellationToken) => Task.FromResult(usage);
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Domain.Accounts;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Answers whether this deployment may send as an account, from the two settings that decide it.</summary>
/// <remarks>
/// <para>
/// An adapter of its own rather than a second interface on the account list, because the answer is composed from two
/// sections an operator edits for different reasons: the installation's own posture, which says whether this process
/// may act outward at all, and the account's switch, which says whether this mailbox is one of the ones it may act as.
/// </para>
/// <para>
/// The posture is read first, so a read-only deployment says so rather than reporting an account nobody turned on. The
/// two are not interchangeable to whoever meets the refusal: one is resolved by an edit to an account and the other by
/// how the installation was started.
/// </para>
/// <para>
/// The account list arrives through the scope's own snapshot, so one work unit reads it under one reload; the posture
/// arrives from the options monitor, because it belongs to no work unit and reloading it is what an operator turning
/// the mode on expects to reach the next send.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this reader.")]
internal sealed class ConfiguredOutgoingSendPermissionReader(
    MailSynchronizationOptions synchronizationSettings,
    IOptionsMonitor<DeploymentOptions> deploymentSettings) : IOutgoingSendPermissionReader
{
    /// <inheritdoc />
    public OutgoingSendRefusalReason? FindRefusal(MailAccountId accountId)
    {
        if (deploymentSettings.CurrentValue.ReadOnly)
        {
            return OutgoingSendRefusalReason.DeploymentIsReadOnly;
        }

        return this.SendingEnabled(accountId) ? null : OutgoingSendRefusalReason.AccountNotEnabled;
    }

    /// <summary>Reports whether an operator has turned sending on for one account.</summary>
    /// <remarks>
    /// An account this snapshot does not name reads as one nobody turned sending on for, which is what it is: the
    /// switch that would admit it exists on no account of this installation. That is also what a reload removing an
    /// account means for a send arriving a moment later.
    /// </remarks>
    private bool SendingEnabled(MailAccountId accountId) =>
        synchronizationSettings.FindConfiguredAccount(accountId)?.Delivery.Enabled ?? false;
}

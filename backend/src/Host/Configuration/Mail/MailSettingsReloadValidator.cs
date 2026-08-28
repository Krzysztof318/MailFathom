// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.OwnerSettings;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Proves a reloaded mail declaration before it becomes the one synchronization is run through.</summary>
/// <remarks>
/// <para>
/// The secrets and the trust anchors are the half a reload has always been judged by. What this adds is the half a
/// start judges through <see cref="DeclaredOwners" /> and a reload could not: the deployment's own
/// <c>MailSynchronization:Accounts</c> names no owner, so it belongs to whichever sole owner a deployment holds, and a
/// deployment serving owners from their own declarations has none. Without this an operator could add an account to
/// that section on a running deployment and have it adopted — and the lookup that resolves a configured account reads
/// that section first, so an owner's mailbox would be run with the transport security, synchronization window, and
/// deletion disposition of a declaration naming nobody while the catalogue went on publishing it under that owner.
/// </para>
/// <para>
/// A candidate that fails leaves the previous declaration serving, which is what keeps the correction reachable: the
/// operator's next edit is read by a process that is still running.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this validator.")]
internal sealed class MailSettingsReloadValidator(
    SecretConfigurationValidator secretValidator,
    ServedMailOwners servedOwners)
{
    /// <summary>Finds everything an operator must fix before a reloaded mail declaration can be published.</summary>
    /// <param name="candidate">The reloaded declaration.</param>
    /// <param name="cancellationToken">Cancels the secret resolution and the certificate loading.</param>
    /// <returns>One message per unusable setting, empty when the candidate is usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidate" /> is <see langword="null" />.</exception>
    internal async Task<IReadOnlyList<string>> FindConfigurationErrorsAsync(
        MailSynchronizationOptions candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var errors = new List<string>(
            await secretValidator.FindMailConfigurationErrorsAsync(candidate, cancellationToken));

        if (candidate.Accounts.Count > 0 && servedOwners.ServesAnyOwnerFromTheirOwnAccounts())
        {
            errors.Add(
                $"{MailSynchronizationOptions.SectionName}:{nameof(MailSynchronizationOptions.Accounts)} declares "
                + $"{candidate.Accounts.Count} mail accounts while this deployment serves owners from their own "
                + $"accounts. That section names no owner, so its accounts belong to whichever sole owner a deployment "
                + $"holds and there is none here: move each of them under the owner who owns it, as an entry of that "
                + $"owner's {DeclaredOwnerOptions.SectionName} entry's {nameof(DeclaredOwnerOptions.MailAccounts)}.");
        }

        return errors;
    }
}

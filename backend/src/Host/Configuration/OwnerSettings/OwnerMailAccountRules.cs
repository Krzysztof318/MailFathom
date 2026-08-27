// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Host.Configuration.Mail;

namespace MailFathom.Host.Configuration.OwnerSettings;

/// <summary>Judges one owner's mail-account declarations, wherever the declarations came from.</summary>
/// <remarks>
/// Three readers ask this and they must not answer differently: the record a <c>settings_accounts</c> row holds, the
/// section a file declares that owner in, and whatever a candidate document would put in either. A rule that held for
/// one of them would be a naming space that changed shape as declarations moved between the file and the row, and the
/// ambiguity it exists to refuse would arrive with the move.
/// </remarks>
internal static class OwnerMailAccountRules
{
    /// <summary>Judges the declarations by every rule a mail account is declared under that needs no clock.</summary>
    /// <param name="mailAccounts">The mail accounts one owner declares.</param>
    /// <param name="memberName">The member the results are reported against, which is the collection the caller bound.</param>
    /// <returns>One result per refusal, empty when the declarations could be this owner's.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="memberName" /> is <see langword="null" />, empty, or white space.</exception>
    /// <remarks>
    /// Each account is judged as one that will be synchronized, which is the strict reading, because a declaration
    /// belonging to an owner is an account they own rather than one a deployment-wide switch left unread. The naming
    /// space is judged within this owner alone, which is what makes two owners each declaring <c>work</c> an ordinary
    /// pair rather than a collision.
    /// </remarks>
    public static IReadOnlyList<ValidationResult> FindRefusals(
        IReadOnlyList<MailSynchronizationAccountOptions>? mailAccounts,
        string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);

        if (mailAccounts is null)
        {
            return [new ValidationResult("An owner's mail accounts must be a list.", [memberName])];
        }

        return
        [
            .. MailAccountNamingSpace.FindCollisions(mailAccounts, memberName),
            .. mailAccounts.SelectMany(account => account.ValidateForSynchronization(synchronizationEnabled: true)),
        ];
    }

    /// <summary>Finds every declared earliest received date that could not mean anything on the supplied date.</summary>
    /// <param name="mailAccounts">The mail accounts one owner declares.</param>
    /// <param name="today">The current date the declared bounds are read against.</param>
    /// <returns>One result per account whose bound lies in the future, empty when every bound is usable.</returns>
    /// <remarks>
    /// The rule asks a question about the current date, which no attribute on a bound graph can reach, so it is
    /// supplied a clock by whoever runs it — exactly as the deployment's own section is, where the same rule is a
    /// custom validator rather than part of the bound graph. A future bound excludes every email the mailbox holds,
    /// which is indistinguishable from synchronization doing nothing.
    /// </remarks>
    public static IReadOnlyList<ValidationResult> FindSynchronizationWindowErrors(
        IReadOnlyList<MailSynchronizationAccountOptions>? mailAccounts,
        DateOnly today) =>
        [.. mailAccounts?.SelectMany(account => account.ValidateSynchronizationWindow(today)) ?? []];
}

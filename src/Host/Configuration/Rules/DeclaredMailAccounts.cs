// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Mail;

namespace MailFathom.Host.Configuration.Rules;

/// <summary>Reads the account identifiers a rule's scope may name, from wherever the accounts are available.</summary>
/// <remarks>
/// <para>
/// A rule's scope is a claim about another section, so judging it needs that section's account identifiers — and the two
/// moments it is judged in reach them differently. Composition has configuration and no container, so it reads the keys;
/// a reload has a container and the published synchronization snapshot, so it reads that. One type holds both, because
/// what counts as a declared account has to be the same answer in both or a rule set startup accepted would be refused
/// on the first reload that changed nothing.
/// </para>
/// <para>
/// Identifiers are trimmed and blanks are dropped, which is what <see cref="Domain.Accounts.MailAccountId" />
/// does to the same text. A blank identifier is the synchronization section's own defect to report, and reporting it
/// again here would name the wrong section.
/// </para>
/// </remarks>
internal static class DeclaredMailAccounts
{
    /// <summary>The configuration section the accounts are declared in.</summary>
    private const string SynchronizationSectionName = "MailSynchronization";

    /// <summary>Reads the declared account identifiers straight from configuration, before any binding has happened.</summary>
    /// <param name="configuration">The configuration the host is composing itself from.</param>
    /// <returns>The identifiers, in the order they are declared.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    public static IReadOnlyCollection<string> ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return
        [
            .. configuration
                .GetSection($"{SynchronizationSectionName}:{nameof(MailSynchronizationOptions.Accounts)}")
                .GetChildren()
                .Select(account => account[nameof(MailSynchronizationAccountOptions.AccountId)]?.Trim())
                .Where(accountId => !string.IsNullOrEmpty(accountId))
                .OfType<string>(),
        ];
    }

    /// <summary>Reads the declared account identifiers from a bound synchronization configuration.</summary>
    /// <param name="settings">The synchronization configuration a reload published, or the one currently in force.</param>
    /// <returns>The identifiers, in the order they are declared.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings" /> is <see langword="null" />.</exception>
    public static IReadOnlyCollection<string> ReadFrom(MailSynchronizationOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return
        [
            .. settings.Accounts
                .Select(account => account.AccountId?.Trim())
                .Where(accountId => !string.IsNullOrEmpty(accountId))
                .OfType<string>(),
        ];
    }
}

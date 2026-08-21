// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Host.Configuration.Rules;

namespace MailFathom.Host.Configuration.Spam;

/// <summary>Judges the configured junk destination against the accounts that would have to file into it.</summary>
/// <remarks>
/// <para>
/// This is the one claim the classification section makes about another section, and it cannot be checked by an attribute
/// on the bound graph: whether <c>role:Junk</c> names anything is a question each account answers separately, in the
/// synchronization section. Asking it here is what turns *this account has no junk folder* into a startup failure naming
/// the account, instead of a message that sits unfiled with nothing said about why.
/// </para>
/// <para>
/// MailFathom never creates the folder —
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>
/// refuses folder management outright — so an account that maps none has no destination that could appear later, and the
/// only repair is a mapping the operator writes.
/// </para>
/// <para>
/// The destination has to be <em>mapped</em> rather than mirrored. A junk folder MailFathom does not mirror is the
/// recommended one, because the point of filing spam is to be rid of it; what a mapping supplies is a name that resolves
/// to a remote folder, which is all a relocation needs.
/// </para>
/// </remarks>
internal static class SpamJunkFolderRules
{
    /// <summary>Finds every account the configured filing would have nowhere to file into.</summary>
    /// <param name="options">The classification section as it currently binds.</param>
    /// <param name="accounts">The accounts the synchronization section declares.</param>
    /// <returns>One result per account without a destination, empty when filing is off or every account has one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static IReadOnlyList<ValidationResult> FindDestinationErrors(
        SpamClassificationOptions options,
        IReadOnlyCollection<DeclaredMailAccount> accounts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(accounts);

        var actions = options.Actions;

        // Nothing is judged unless filing is actually switched on. A destination written beside switches that are off is
        // an operator preparing a configuration, and refusing it would make staging the change impossible.
        if (!options.Enabled || !actions.MoveToJunkFolder)
        {
            return [];
        }

        var destination = actions.Destination;

        return
        [
            .. accounts
                .Where(account => !account.Maps(destination))
                .Select(account => new ValidationResult(
                    $"{SpamClassificationOptions.SectionName} files junk into '{destination}', and account '{account.AccountId}' maps no such folder. Map it in the account's Folders, or name a folder that account has; MailFathom does not create one.",
                    [nameof(SpamClassificationOptions.Actions)])),
        ];
    }
}

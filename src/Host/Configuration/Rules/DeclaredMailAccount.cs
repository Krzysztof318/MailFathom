// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Actions;
using MailFathom.Domain.Folders;

namespace MailFathom.Host.Configuration.Rules;

/// <summary>One account as a rule set is judged against it: its identifier, its mirrored folders, and what it permits.</summary>
/// <param name="AccountId">The identifier a rule's scope names, compared exactly as the synchronization section writes it.</param>
/// <param name="MirroredFolders">
/// The folders this account mirrors, which are the folders a rule may name as a destination. A mapped folder the account
/// does not mirror is deliberately absent: nothing binds such a folder to a remote one, so a rule filing into it could
/// never resolve a path to file into.
/// </param>
/// <param name="PermittedRuleActions">Which of the four changes a rule may ask for on this account.</param>
internal sealed record DeclaredMailAccount(
    string AccountId,
    IReadOnlyCollection<DeclaredMailFolder> MirroredFolders,
    MailRuleActionPermissions PermittedRuleActions)
{
    /// <summary>Reports whether a rule's destination names a folder this account mirrors.</summary>
    /// <param name="destination">The alias or role the rule wrote.</param>
    /// <returns><see langword="true" /> when one mirrored folder of this account answers to that name.</returns>
    /// <remarks>
    /// The two kinds of name are answered together rather than by two rules that could drift apart, which is what makes
    /// a rule filing into <c>role:Junk</c> refused for exactly the accounts that map no mirrored junk folder — and
    /// accepted for the ones that do, whatever each of them called it.
    /// </remarks>
    internal bool Mirrors(MailFolderReference destination) =>
        this.MirroredFolders.Any(folder => folder.IsNamedBy(destination));
}

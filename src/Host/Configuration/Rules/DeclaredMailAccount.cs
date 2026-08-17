// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Actions;
using MailFathom.Domain.Folders;

namespace MailFathom.Host.Configuration.Rules;

/// <summary>One account as a rule set is judged against it: its identifier, its mapped folders, and what it permits.</summary>
/// <param name="AccountId">The identifier a rule's scope names, compared exactly as the synchronization section writes it.</param>
/// <param name="MappedFolders">
/// Every folder this account maps, whether or not it mirrors one, because a mapping is all a destination needs. A folder
/// the account only maps is resolved when a change first files into it rather than by a run of its own, so filing into
/// one is reachable in exactly the way filing into a mirrored folder is.
/// </param>
/// <param name="PermittedRuleActions">Which changes a rule may ask for on this account.</param>
internal sealed record DeclaredMailAccount(
    string AccountId,
    IReadOnlyCollection<DeclaredMailFolder> MappedFolders,
    MailRuleActionPermissions PermittedRuleActions)
{
    /// <summary>Reports whether a rule's destination names a folder this account maps.</summary>
    /// <param name="destination">The alias or role the rule wrote.</param>
    /// <returns><see langword="true" /> when one mapped folder of this account answers to that name.</returns>
    /// <remarks>
    /// The two kinds of name are answered together rather than by two rules that could drift apart, which is what makes
    /// a rule filing into <c>role:Junk</c> refused for exactly the accounts that map no junk folder — and accepted for
    /// the ones that do, whatever each of them called it and whether or not they mirror it.
    /// </remarks>
    internal bool Maps(MailFolderReference destination) =>
        this.MappedFolders.Any(folder => folder.IsNamedBy(destination));
}

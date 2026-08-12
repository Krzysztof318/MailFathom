// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Actions;
using MailFathom.Domain.Folders;

namespace MailFathom.Host.Configuration.Rules;

/// <summary>One account as a rule set is judged against it: its identifier, its mirrored folders, and what it permits.</summary>
/// <param name="AccountId">The identifier a rule's scope names, compared exactly as the synchronization section writes it.</param>
/// <param name="MirroredFolderAliases">
/// The aliases of the folders this account mirrors, which are the folders a rule may name as a destination. A mapped
/// folder the account does not mirror is deliberately absent: nothing binds such an alias to a remote folder, so a rule
/// filing into one could never resolve a path to file into.
/// </param>
/// <param name="PermittedRuleActions">Which of the four changes a rule may ask for on this account.</param>
internal sealed record DeclaredMailAccount(
    string AccountId,
    IReadOnlyCollection<MailFolderAlias> MirroredFolderAliases,
    MailRuleActionPermissions PermittedRuleActions);

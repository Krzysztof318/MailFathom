// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Accounts;

/// <summary>The owner's accounts and how current each one's local copy is, as a reader that draws no mail sees them.</summary>
/// <param name="SynchronizationEnabled">Whether the deployment refreshes the local copy of these accounts at all.</param>
/// <param name="Accounts">The owner's accounts, ordered as the catalog orders them, empty when they own none.</param>
/// <remarks>
/// The switch is the deployment's rather than the owner's, and it is answered beside the accounts for the reason
/// <see cref="MailAccountDirectory" /> answers it: an account that last synchronized a week ago is a different fact on a
/// deployment that is trying every ten minutes and on one that stopped trying, and no per-account value says which.
/// </remarks>
public sealed record MailAccountFreshnessDirectory(
    bool SynchronizationEnabled,
    IReadOnlyList<MailAccountFreshness> Accounts);

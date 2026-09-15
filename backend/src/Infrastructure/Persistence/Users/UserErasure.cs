// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>What erasing one user removed.</summary>
/// <param name="UserErased">Whether a user record was there to remove, so a repeat is reported as the no-op it is.</param>
/// <param name="RowsErasedBesideTheCascade">
/// How many rows the seam took itself, from the tables that name a mail account without a foreign key onto one. It is
/// stated because those are exactly the rows no constraint would have removed, so a number that falls to zero while
/// such a table still exists is the failure this record is written to make visible.
/// </param>
/// <param name="UnquiescedAccount">
/// The account the transaction found it was about to delete while something could still be writing to it, or
/// <see langword="null" /> when nothing stopped the walk. It is set only where the walk wrote nothing at all.
/// </param>
internal readonly record struct UserErasure(
    bool UserErased,
    int RowsErasedBesideTheCascade,
    Guid? UnquiescedAccount = null)
{
    /// <summary>An erasure abandoned before its first write, because one account could not be shown still.</summary>
    /// <param name="account">The account that stopped it.</param>
    /// <returns>The refusal, which removed nothing.</returns>
    internal static UserErasure Refused(Guid account) => new(false, 0, account);
}

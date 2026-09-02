// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence;

/// <summary>Splits what a bounded keyset read returned into the page it holds and the answer about a following one.</summary>
/// <remarks>
/// <para>
/// Every keyset reader here reads one more row than the page holds, which is how the answer says whether a following
/// page exists without a second count query over the same filtered set. The extra row is never presented: it is read so
/// that its existence can be observed, and then dropped.
/// </para>
/// <para>
/// A read that reached beyond the page is by construction a read that filled it, so a caller building a cursor from the
/// last row of the page needs no second guard against an empty one: the boundary row exists wherever a following page
/// does.
/// </para>
/// </remarks>
internal static class KeysetPageSplit
{
    /// <summary>Splits the rows one bounded read returned.</summary>
    /// <typeparam name="TRow">The row shape the read projected.</typeparam>
    /// <param name="readRows">The rows the read returned, taken as one more than the page holds.</param>
    /// <param name="pageSize">The number of rows the page holds.</param>
    /// <returns>The page, and whether the read reached past it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="readRows" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="pageSize" /> is not positive.</exception>
    internal static (TRow[] Page, bool HasMore) Of<TRow>(TRow[] readRows, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(readRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

        return readRows.Length > pageSize
            ? (readRows[..pageSize], true)
            : (readRows, false);
    }
}

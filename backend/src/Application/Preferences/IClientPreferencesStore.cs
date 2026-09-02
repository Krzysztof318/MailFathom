// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Preferences;

/// <summary>Holds one person's client preferences, and hands them back to whichever machine they next sign in from.</summary>
/// <remarks>
/// <para>
/// One read and one write, each for one owner, so there is no shape here that touches two people's preferences and no
/// query that could compose a roster out of them. Both are key lookups for that reason rather than for a plan's.
/// </para>
/// <para>
/// The write is last-write-wins and states no version. Two of one person's own devices disagreeing about a switch they
/// both set is not a loss worth a conflict screen over a checkbox, and there is no third party whose change could be
/// overwritten — the record's superseded refusal exists because an administrator writes that document too.
/// </para>
/// </remarks>
public interface IClientPreferencesStore
{
    /// <summary>Reads what one person set about their own client.</summary>
    /// <param name="owner">The owner whose preferences are read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>What they set, or <see langword="null" /> where they have set nothing.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <exception cref="System.Text.Json.JsonException">Thrown when the stored row is not a document of preferences, which is a row something other than this store wrote.</exception>
    /// <remarks>Having set nothing is answered apart from having set the defaults, because a client drawing a first run may want to know which it is; both are rendered the same way today.</remarks>
    Task<ClientPreferences?> ReadAsync(MailOwnerId owner, CancellationToken cancellationToken);

    /// <summary>Replaces what one person set about their own client.</summary>
    /// <param name="owner">The owner whose preferences are written.</param>
    /// <param name="preferences">The whole set, since the document is closed and a write states all of it.</param>
    /// <param name="cancellationToken">Cancels the commit.</param>
    /// <returns><see langword="true" /> when the write landed, and <see langword="false" /> when this deployment holds no such owner.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="preferences" /> is <see langword="null" />.</exception>
    /// <remarks>An owner this deployment does not hold is reported rather than raised, because the caller is a person whose row was erased under a credential that has not yet been withdrawn and the answer to them is that there is nothing here of theirs.</remarks>
    Task<bool> SaveAsync(MailOwnerId owner, ClientPreferences preferences, CancellationToken cancellationToken);
}

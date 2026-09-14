// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.Records;

/// <summary>Holds the user and mail-account records this replica refused, so an operator meets them without reading logs.</summary>
/// <remarks>
/// <para>
/// A report rather than a guarantee, which is what makes one process's dictionary the right shape for it: every replica
/// binds the same rows and refuses the same documents, so what this holds is this replica's reading of them and an
/// operator reading one replica learns what every replica did. Nothing decides anything on it.
/// </para>
/// <para>
/// The entries are kept by user because that is the unit the records are re-read in: a user's document and every mail
/// account assigned to them are bound together, so a republication replaces whatever the last one left rather than
/// adding to it, and a user whose record binds leaves nothing behind. An organization is not kept here — no roster
/// binds one, so the listing reads the rows when it is asked.
/// </para>
/// <para>
/// What it can grow to is the roster's own ceiling rather than a bound of its own: at most the users one deployment
/// serves, each with at most the accounts one user is assigned, which is what a deployment refusing every record it
/// holds would leave here. Nothing accumulates beyond that, because each user's entry replaces the last.
/// </para>
/// </remarks>
internal sealed class HeldBackRecords
{
    private readonly Lock mutex = new();
    private readonly Dictionary<MailUserId, IReadOnlyList<HeldBackRecord>> byUser = [];

    /// <summary>Gets every record this replica currently holds back, in no particular order.</summary>
    internal IReadOnlyList<HeldBackRecord> Current
    {
        get
        {
            lock (this.mutex)
            {
                return [.. this.byUser.Values.SelectMany(records => records)];
            }
        }
    }

    /// <summary>States what one user's records left refused, replacing whatever the last reading of them left.</summary>
    /// <param name="user">The user the records were read for.</param>
    /// <param name="records">What was refused, which is empty when the user and every account assigned to them bound.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="records" /> is <see langword="null" />.</exception>
    internal void Replace(MailUserId user, IReadOnlyList<HeldBackRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        lock (this.mutex)
        {
            if (records.Count == 0)
            {
                this.byUser.Remove(user);

                return;
            }

            this.byUser[user] = [.. records];
        }
    }

    /// <summary>Drops everything held back for a user this deployment no longer holds.</summary>
    /// <param name="user">The erased user.</param>
    internal void Cleared(MailUserId user)
    {
        lock (this.mutex)
        {
            this.byUser.Remove(user);
        }
    }
}
